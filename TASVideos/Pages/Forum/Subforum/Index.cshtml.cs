﻿using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TASVideos.Data;
using TASVideos.Data.Entity;
using TASVideos.Data.Entity.Forum;
using TASVideos.Models;
using TASVideos.Services;

namespace TASVideos.Pages.Forum.Subforum
{
	[AllowAnonymous]
	public class IndexModel : BasePageModel
	{
		private readonly ApplicationDbContext _db;

		public IndexModel(
			ApplicationDbContext db,
			UserManager userManager) 
			: base(userManager)
		{
			_db = db;
		}

		[FromQuery]
		public ForumRequest Search { get; set; }

		[FromRoute]
		public int Id { get; set; }

		public ForumModel Forum { get; set; }

		public async Task<IActionResult> OnGet()
		{
			using (await _db.Database.BeginTransactionAsync())
			{
				Forum = await _db.Forums
					.ExcludeRestricted(UserHas(PermissionTo.SeeRestrictedForums))
					.Select(f => new ForumModel
					{
						Id = f.Id,
						Name = f.Name,
						Description = f.Description
					})
					.SingleOrDefaultAsync(f => f.Id == Id);

				if (Forum == null)
				{
					return NotFound();
				}

				var rowsToSkip = Search.GetRowsToSkip();
				var rowCount = await _db.ForumTopics
					.ForForum(Id)
					.CountAsync();

				var results = await _db.ForumTopics
					.ForForum(Id)
					.Select(ft => new ForumModel.ForumTopicEntry
					{
						Id = ft.Id,
						Title = ft.Title,
						CreateUserName = ft.CreateUserName,
						CreateTimestamp = ft.CreateTimeStamp,
						Type = ft.Type,
						Views = ft.Views,
						PostCount = ft.ForumPosts.Count,
						LastPost = ft.ForumPosts.Max(fp => (DateTime?)fp.CreateTimeStamp)
					})
					.OrderByDescending(ft => ft.Type == ForumTopicType.Sticky)
					.ThenByDescending(ft => ft.Type == ForumTopicType.Announcement)
					.ThenByDescending(ft => ft.LastPost)
					.Skip(rowsToSkip)
					.Take(Search.PageSize)
					.ToListAsync();

				Forum.Topics = new PageOf<ForumModel.ForumTopicEntry>(results)
				{
					PageSize = Search.PageSize,
					CurrentPage = Search.CurrentPage,
					RowCount = rowCount,
					SortDescending = Search.SortDescending,
					SortBy = Search.SortBy
				};

				Forum.Description = RenderHtml(Forum.Description);
				return Page();
			}
		}
	}
}
