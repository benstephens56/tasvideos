﻿using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TASVideos.Core.Services;
using TASVideos.Core.Services.ExternalMediaPublisher;
using TASVideos.Data;
using TASVideos.Data.Entity;

namespace TASVideos.Pages.Publications;

[RequirePermission(PermissionTo.EditPublicationFiles)]
public class EditFilesModel : BasePageModel
{
	private static readonly List<FileType> PublicationFileTypes = Enum
		.GetValues(typeof(FileType))
		.Cast<FileType>()
		.ToList();

	private readonly ApplicationDbContext _db;
	private readonly ExternalMediaPublisher _publisher;
	private readonly IMediaFileUploader _uploader;
	private readonly IPublicationMaintenanceLogger _publicationMaintenanceLogger;

	public EditFilesModel(
		ApplicationDbContext db,
		ExternalMediaPublisher publisher,
		IMediaFileUploader uploader,
		IPublicationMaintenanceLogger publicationMaintenanceLogger)
	{
		_db = db;
		_publisher = publisher;
		_uploader = uploader;
		_publicationMaintenanceLogger = publicationMaintenanceLogger;
	}

	public IEnumerable<SelectListItem> AvailableTypes =
		PublicationFileTypes
			.Where(t => t != FileType.MovieFile)
			.Select(t => new SelectListItem
			{
				Text = t.ToString(),
				Value = ((int)t).ToString()
			});

	[FromRoute]
	public int Id { get; set; }

	[BindProperty]
	public string Title { get; set; } = "";

	public ICollection<PublicationFile> Files { get; set; } = new List<PublicationFile>();

	[Required]
	[BindProperty]
	public IFormFile? NewFile { get; set; }

	[Required]
	[BindProperty]
	public FileType Type { get; set; }

	[BindProperty]
	[StringLength(250)]
	public string? Description { get; set; }

	public async Task<IActionResult> OnGet()
	{
		var title = await _db.Publications
			.Where(p => p.Id == Id)
			.Select(p => p.Title)
			.SingleOrDefaultAsync();

		if (title == null)
		{
			return NotFound();
		}

		Title = title;
		Files = await _db.PublicationFiles
			.ForPublication(Id)
			.ToListAsync();

		return Page();
	}

	public async Task<IActionResult> OnPost()
	{
		if (!ModelState.IsValid)
		{
			Files = await _db.PublicationFiles
				.ForPublication(Id)
				.ToListAsync();

			return Page();
		}

		string path = "";
		if (Type == FileType.Screenshot)
		{
			path = await _uploader.UploadScreenshot(Id, NewFile!, Description);
		}

		string log = $"Added {Type} file {path}";
		SuccessStatusMessage(log);
		await _publicationMaintenanceLogger.Log(Id, User.GetUserId(), log);
		await _publisher.SendPublicationEdit(
			$"{Id}M edited by {User.Name()}",
			$"{log} | {Title}",
			$"{Id}M");

		return RedirectToPage("EditFiles", new { Id });
	}

	public async Task<IActionResult> OnPostDelete(int publicationFileId)
	{
		var file = await _uploader.DeleteFile(publicationFileId);

		if (file is not null)
		{
			string log = $"Deleted {file.Type} file {file.Path}";
			SuccessStatusMessage(log);
			await _publicationMaintenanceLogger.Log(Id, User.GetUserId(), log);
			await _publisher.SendPublicationEdit(
				$"{Id}M edited by {User.Name()}",
				$"{log}",
				$"{Id}M");
		}

		return RedirectToPage("EditFiles", new { Id });
	}
}
