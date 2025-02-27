﻿using System.ComponentModel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TASVideos.Core.Services;
using TASVideos.Core.Services.ExternalMediaPublisher;
using TASVideos.Data;
using TASVideos.Data.Entity;
using TASVideos.Data.Entity.Forum;
using TASVideos.Pages.Forum.Topics.Models;

namespace TASVideos.Pages.Forum.Topics;

[RequirePermission(PermissionTo.CreateForumTopics)]
public class CreateModel : BaseForumModel
{
	private readonly UserManager _userManager;
	private readonly ApplicationDbContext _db;
	private readonly ExternalMediaPublisher _publisher;
	private readonly IForumService _forumService;

	public CreateModel(
		UserManager userManager,
		ApplicationDbContext db,
		ExternalMediaPublisher publisher,
		IForumService forumService)
	{
		_userManager = userManager;
		_db = db;
		_publisher = publisher;
		_forumService = forumService;
	}

	[FromRoute]
	public int ForumId { get; set; }

	[BindProperty]
	public TopicCreateModel Topic { get; set; } = new();

	[BindProperty]
	[DisplayName("Watch Topic for Replies")]
	public bool WatchTopic { get; set; } = true;

	public AvatarUrls UserAvatars { get; set; } = new(null, null);

	public async Task<IActionResult> OnGet()
	{
		var seeRestricted = User.Has(PermissionTo.SeeRestrictedForums);
		var topic = await _db.Forums
			.ExcludeRestricted(seeRestricted)
			.Where(f => f.Id == ForumId)
			.Select(f => new TopicCreateModel
			{
				ForumName = f.Name
			})
			.SingleOrDefaultAsync();

		if (topic == null)
		{
			return NotFound();
		}

		Topic = topic;
		UserAvatars = await _forumService.UserAvatars(User.GetUserId());
		return Page();
	}

	public async Task<IActionResult> OnPost(PollCreateModel poll)
	{
		if (!ModelState.IsValid)
		{
			UserAvatars = await _forumService.UserAvatars(User.GetUserId());
			return Page();
		}

		var forum = await _db.Forums.SingleOrDefaultAsync(f => f.Id == ForumId);
		if (forum == null)
		{
			return NotFound();
		}

		if (forum.Restricted && !User.Has(PermissionTo.SeeRestrictedForums))
		{
			return NotFound();
		}

		int userId = User.GetUserId();

		var topic = new ForumTopic
		{
			Type = Topic.Type,
			Title = Topic.Title,
			PosterId = userId,
			ForumId = ForumId
		};

		_db.ForumTopics.Add(topic);

		// TODO: catch DbConcurrencyException
		await _db.SaveChangesAsync();

		await _forumService.CreatePost(new PostCreateDto(
			ForumId,
			topic.Id,
			null,
			Topic.Post,
			userId,
			User.Name(),
			Topic.Mood,
			IpAddress,
			WatchTopic));

		if (User.Has(PermissionTo.CreateForumPolls) && poll.IsValid)
		{
			await _forumService.CreatePoll(
				topic,
				new PollCreateDto(poll.Question, poll.DaysOpen, poll.MultiSelect, poll.PollOptions));
		}

		await _publisher.SendForum(
			forum.Restricted,
			$"New Topic by {User.Name()}",
			$"{forum.ShortName}: {Topic.Title}",
			$"Forum/Topics/{topic.Id}");

		await _userManager.AssignAutoAssignableRolesByPost(User.GetUserId());

		return RedirectToPage("Index", new { topic.Id });
	}
}
