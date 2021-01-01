using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace TASVideos.WikiEngine.AST
{
	public enum NodeType
	{
		Text,
		Element,
		IfModule,
		Module
	}

	public interface INode
	{
		[JsonConverter(typeof(StringEnumConverter))]
		NodeType Type { get; }
		int CharStart { get; }
		int CharEnd { get; set; }
		INode Clone();
		void WriteHtmlDynamic(TextWriter w, IWriterHelper h);

		/// <summary>
		/// Get the combined text content of this Node.  May not return useful values for foreign components (Modules)
		/// </summary>
		string InnerText(IWriterHelper h);

		/// <summary>
		/// debugging output of all of the data in this node
		/// </summary>
		void DumpContentDescriptive(TextWriter w, string padding);

		/// <summary>
		/// Clones this node for use in a TOC.  Some things like anchors are removed
		/// </summary>
		IEnumerable<INode> CloneForToc();
	}

	public interface IWriterHelper
	{
		bool CheckCondition(string condition);
		void RunViewComponent(TextWriter w, string name, string pp);
		bool AddTdStyleFilter(string pp);
		string? RunTdStyleFilters(string text);
	}

	/// <summary>
	/// A fake IWriterHelper which can give "good enough" results if a static context is needed
	/// </summary>
	public class NullWriterHelper : IWriterHelper
	{
		public bool AddTdStyleFilter(string pp)
		{
			return true;
		}

		public bool CheckCondition(string condition)
		{
			return false;
		}

		public string? RunTdStyleFilters(string text)
		{
			return null;
		}

		public void RunViewComponent(TextWriter w, string name, string pp)
		{
		}

		private NullWriterHelper()
		{
		}

		public static readonly NullWriterHelper Instance = new();
	}

	public interface INodeWithChildren : INode
	{
		List<INode> Children { get; }
	}

	public class Text : INode
	{
		public NodeType Type => NodeType.Text;
		public string Content { get; }
		public int CharStart { get; }
		public int CharEnd { get; set; }
		public Text(int charStart, string content)
		{
			CharStart = charStart;
			Content = content;
		}

		public void WriteHtmlDynamic(TextWriter w, IWriterHelper h)
		{
			foreach (var c in Content)
			{
				switch (c)
				{
					case '<':
						w.Write("&lt;");
						break;
					case '&':
						w.Write("&amp;");
						break;
					default:
						w.Write(c);
						break;
				}
			}			
		}

		public INode Clone()
		{
			return (Text)MemberwiseClone();
		}

		public string InnerText(IWriterHelper h)
		{
			return Content;
		}

		public void DumpContentDescriptive(TextWriter w, string padding)
		{
			if (Content.Any(c => c < 0x20 && c != '\n' && c != '\t'))
			{
				w.Write(padding);
				w.WriteLine("$UNPRINTABLE TEXT!!!");
			}
			else
			{
				var first = true;
				foreach (var s in Content.Split('\n'))
				{
					if (first)
					{
						first = false;
					}
					else
					{
						w.Write(padding);
						w.WriteLine("$LF");
					}

					if (s.Length > 0)
					{
						w.Write(padding);
						w.Write('"');
						w.WriteLine(s);
					}
				}
			}
		}

		public IEnumerable<INode> CloneForToc()
		{
			return new[] { Clone() };
		}
	}
	
	public class Element : INodeWithChildren
	{
		private static readonly Regex AllowedTagNames = new("^[a-z0-9]+$");
		private static readonly Regex AllowedAttributeNames = new("^[a-z\\-]+$");
		private static readonly HashSet<string> VoidTags = new()
		{
			"area", "base", "br", "col", "embed", "hr", "img", "input",
			"keygen", "link", "meta", "param", "source", "track", "wbr"
		};
		public NodeType Type => NodeType.Element;
		public List<INode> Children { get; private set; } = new();
		public IDictionary<string, string> Attributes { get; private set; } = new Dictionary<string, string>();
		public string Tag { get; }
		public int CharStart { get; }
		public int CharEnd { get; set; }
		public Element(int charStart, string tag)
		{
			if (!AllowedTagNames.IsMatch(tag))
			{
				throw new InvalidOperationException("Invalid tag name");
			}

			if (tag == "script" || tag == "style")
			{
				// we don't escape for these
				throw new InvalidOperationException("Unsupported tag!");
			}

			CharStart = charStart;
			Tag = tag;
		}

		public Element(int charStart, string tag, IEnumerable<INode> children)
			: this(charStart, tag)
		{
			Children.AddRange(children);
		}

		public Element(int charStart, string tag, IEnumerable<KeyValuePair<string, string>> attributes, IEnumerable<INode> children)
			: this(charStart, tag, children)
		{
			foreach (var kvp in attributes)
				Attributes.Add(kvp.Key, kvp.Value);
		}

		public void WriteHtmlDynamic(TextWriter w, IWriterHelper h)
		{
			if (VoidTags.Contains(Tag) && Children.Count > 0)
			{
				throw new InvalidOperationException("Void tag with child content!");
			}

			IEnumerable<KeyValuePair<string, string>> attrs = Attributes;
			if (Tag == "td")
			{
				var style = h.RunTdStyleFilters(InnerText(h));
				if (style != null)
				{
					attrs = attrs.Concat(new[] { new KeyValuePair<string, string>("style", style) });
				}
			}

			w.Write('<');
			w.Write(Tag);
			foreach (var a in attrs)
			{
				if (!AllowedAttributeNames.IsMatch(a.Key))
				{
					throw new InvalidOperationException("Invalid attribute name");
				}

				w.Write(' ');
				w.Write(a.Key);
				w.Write("=\"");
				foreach (var c in a.Value)
				{
					switch (c)
					{
						case '<':
							w.Write("&lt;");
							break;
						case '&':
							w.Write("&amp;");
							break;
						case '"':
							w.Write("&quot;");
							break;
						default:
							w.Write(c);
							break;
					}
				}

				w.Write('"');
			}

			if (VoidTags.Contains(Tag))
			{
				w.Write(" />");
			}
			else
			{
				w.Write('>');
				foreach (var c in Children)
					c.WriteHtmlDynamic(w, h);
				w.Write("</");
				w.Write(Tag);
				w.Write('>');
			}			
		}

		public INode Clone()
		{
			var ret = (Element)MemberwiseClone();
			ret.Children = Children.Select(c => c.Clone()).ToList();
			ret.Attributes = new Dictionary<string, string>(Attributes);
			return ret;
		}

		public string InnerText(IWriterHelper h)
		{
			return string.Join("", Children.Select(c => c.InnerText(h)));
		}

		public void DumpContentDescriptive(TextWriter w, string padding)
		{
			w.Write(padding);
			w.Write('[');
			w.Write(Tag);
			w.Write(' ');
			foreach (var kvp in Attributes.OrderBy(z => z.Key))
			{
				w.Write(kvp.Key);
				w.Write('=');
				w.Write(kvp.Value);
				w.Write(' ');
			}

			w.WriteLine();
			foreach (var child in Children)
				child.DumpContentDescriptive(w, padding + '\t');
			w.Write(padding);
			w.Write(']');
			w.Write(Tag);
			w.WriteLine();
		}

		private static readonly HashSet<string> TocTagBlacklist = new()
		{
			"a", "br"
		};

		public IEnumerable<INode> CloneForToc()
		{
			var children = Children.SelectMany(n => n.CloneForToc());
			if (TocTagBlacklist.Contains(Tag))
			{
				return children;
			}

			var ret = (Element)MemberwiseClone();
			ret.Children = children.ToList();
			ret.Attributes = new Dictionary<string, string>(Attributes);
			return new[] { ret };
		}
	}

	public class IfModule : INodeWithChildren
	{
		public NodeType Type => NodeType.IfModule;
		public List<INode> Children { get; private set; } = new();
		public string Condition { get; }
		public int CharStart { get; }
		public int CharEnd { get; set; }
		public IfModule(int charStart, string condition)
		{
			CharStart = charStart;
			Condition = condition;
		}

		public IfModule(int charStart, string condition, IEnumerable<INode> children)
			: this(charStart, condition)
		{
			Children.AddRange(children);
		}

		public void WriteHtmlDynamic(TextWriter w, IWriterHelper h)
		{
			if (h.CheckCondition(Condition))
			{
				foreach (var c in Children)
					c.WriteHtmlDynamic(w, h);
			}
		}

		public INode Clone()
		{
			var ret = (IfModule)MemberwiseClone();
			ret.Children = Children.Select(c => c.Clone()).ToList();
			return ret;
		}

		public string InnerText(IWriterHelper h)
		{
			return h.CheckCondition(Condition)
				? string.Join("", Children.Select(c => c.InnerText(h)))
				: "";
		}

		public void DumpContentDescriptive(TextWriter w, string padding)
		{
			w.Write(padding);
			w.Write("?IF ");
			w.Write(Condition);
			w.WriteLine();
			foreach (var child in Children)
			{
				child.DumpContentDescriptive(w, padding + '\t');
			}

			w.Write(padding);
			w.Write("?ENDIF ");
			w.Write(Condition);
			w.WriteLine();
		}

		public IEnumerable<INode> CloneForToc()
		{
			return new[] { Clone() };
		}
	}

	public class Module : INode
	{
		private static readonly Dictionary<string, string> ModuleNameMaps = new()
		{
			["__wikilink"] = "WikiLink",
			["awards"] = "Awards",
			["brokenlinks"] = "BrokenLinks",
			["activetab"] = "ActiveTab",
			["displayminimovie"] = "DisplayMiniMovie",
			["editoractivity"] = "EditorActivity",
			["firsteditiontas"] = "FirstEditionTas",
			["frames"] = "Frames",
			["frontpagesubmissionlist"] = "FrontpageSubmissionList",
			["gamename"] = "GameName",
			["gamesubpages"] = "GameSubPages",
			["listparents"] = "ListParents",
			["listsubpages"] = "ListSubPages",
			["mediaposts"] = "MediaPosts",
			["platformframerates"] = "PlatformFramerates",
			["rejectedsubmissions"] = "RejectedSubmissions",
			["submittableformats"] = "SupportedMovieTypes",
			["tabularmovielist"] = "TabularMovieList",
			["topicfeed"] = "TopicFeed",
			["unmirroredmovies"] = "UnmirroredMovies",
			["user_name"] = "UserName",
			["usergetwikiname"] = "UserGetWikiName",
			["usermovies"] = "UserMovies",
			["welcome"] = "Welcome",
			["wikigetcurrenteditlink"] = "CurrentEditLink",
			["wikiorphans"] = "WikiOrphans",
			["wikitextchangelog"] = "WikiTextChangeLog",
			["youtube"] = "Youtube",
			["listlanguages"] = "ListLanguages",
			["wikiusers"] = "WikiUsers",
			["movieslist"] = "MoviesList",
			["moviesgamelist"] = "MoviesGameList",
			["displaymovie"] = "DisplayMovies",
			["moviesbyplayer"] = "MoviesByAuthor",
			["feedlog"] = "FeedLog",
			["timesincedate"] = "TimeSinceDate"
		};
		public NodeType Type => NodeType.Module; 
		public string Text { get; }
		public int CharStart { get; }
		public int CharEnd { get; set; }
		public Module(int charStart, int charEnd, string text)
		{
			CharStart = charStart;
			CharEnd = charEnd;
			Text = text;
		}

		public void WriteHtmlDynamic(TextWriter w, IWriterHelper h)
		{
			var pp = Text.Split(new[] { '|' }, 2);
			var moduleName = pp[0];
			var moduleParams = pp.Length > 1 ? pp[1] : "";
			if (moduleName.ToLower() == "settableattributes")
			{
				if (!h.AddTdStyleFilter(moduleParams))
				{
					var div = new Element(CharStart, "div") { CharEnd = CharEnd };
					div.Children.Add(new Text(CharStart, "Module Error for settableattributes: Couldn't parse parameter string " + moduleParams) { CharEnd = CharEnd });
					div.Attributes["class"] = "module-error";
					div.WriteHtmlDynamic(w, h);
				}
			}
			else if (ModuleNameMaps.TryGetValue(moduleName.ToLower(), out string? realModuleName))
			{
				h.RunViewComponent(w, realModuleName, moduleParams);
			}
			else
			{
				var div = new Element(CharStart, "div") { CharEnd = CharEnd };
				div.Children.Add(new Text(CharStart, "Unknown module " + moduleName) { CharEnd = CharEnd });
				div.Attributes["class"] = "module-error";
				div.WriteHtmlDynamic(w, h);
			}
		}

		public INode Clone()
		{
			return (Module)MemberwiseClone();
		}

		public string InnerText(IWriterHelper h)
		{
			// could actually run the module here... but no.
			return "";
		}

		public void DumpContentDescriptive(TextWriter w, string padding)
		{
			w.Write(padding);
			w.Write('(');
			w.Write(Text);
			w.WriteLine(')');
		}

		public IEnumerable<INode> CloneForToc()
		{
			return Enumerable.Empty<INode>();
		}
	}
}
