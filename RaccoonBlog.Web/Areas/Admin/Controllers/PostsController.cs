using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Mime;
using System.Web.Mvc;
using HibernatingRhinos.Loci.Common.Extensions;
using HibernatingRhinos.Loci.Common.Models;
using RaccoonBlog.Web.Helpers;
using RaccoonBlog.Web.Helpers.Attributes;
using RaccoonBlog.Web.Infrastructure.AutoMapper;
using RaccoonBlog.Web.Infrastructure.Common;
using RaccoonBlog.Web.Models;
using RaccoonBlog.Web.Services;
using RaccoonBlog.Web.ViewModels;
using Raven.Abstractions.Commands;
using Raven.Abstractions.Data;
using Raven.Json.Linq;



namespace RaccoonBlog.Web.Areas.Admin.Controllers
{
    public class PostsController : AdminController
    {
        public ActionResult Index()
        {
            // the actual UI is handled via JavaScript
            return View("List");
        }


        [HttpGet]
        public ActionResult Add()
        {
            return View("Edit", new PostInput
            {
                AllowComments = true,
                ContentType = DynamicContentType.Html,
                CreatedAt = DateTimeOffset.Now,


            });

        }


        [HttpGet]
        public ActionResult Edit(int id)
        {
            var post = RavenSession.Load<Post>(id);
            if (post == null)
                return HttpNotFound("Post does not exist.");
            return View(post.MapTo<PostInput>());
        }


        //START//
        public class TempInput
        {
            public string Title { get; set; }
            public string Body { get; set; }
            public string CreatedAt { get; set; }
            public string PublishAt { get; set; }
            public string Tags { get; set; }
            public string AllowComments { get; set; }
            public string Id { get; set; }
        }
        [HttpPost]
        [ValidateInput(true)]
        public ActionResult Update(TempInput sendInfo)
        //public ActionResult Update(PostInput input, string publish)////////////////////////
        {



            //var post = RavenSession.Load<Post>(input.Id) ?? new Post { CreatedAt = DateTimeOffset.Now };//Post is an empty tool here
            ////////////////


            sendInfo.Id = "0";
            var post = RavenSession.Load<Post>(sendInfo.Id) ?? new Post { CreatedAt = DateTimeOffset.Now };//Post is an empty tool here


            //input.MapPropertiesToInstance(post);//Entering data  ///////////////////////

            post.Title = sendInfo.Title;
            if (sendInfo.AllowComments == "on")
                post.AllowComments = true;
            else post.AllowComments = false;
            post.Body = sendInfo.Body;
            post.CreatedAt = DateTimeOffset.Now;

            //Convert PublishAt (string) into dateTime
            string mm = sendInfo.PublishAt.Substring(0, 2);
            int mmInt = int.Parse(mm);
            string dd = sendInfo.PublishAt.Substring(3,2);
            int ddInt = int.Parse(dd);
            string yy = sendInfo.PublishAt.Substring(6, 4);
            int yyInt = int.Parse(yy);
            // Convert into date
            DateTime TempDate=new DateTime(yyInt,mmInt,ddInt);
            // Convert into DateTimeOffset
            DateTimeOffset TempDateOffSet=new DateTimeOffset(TempDate);
            // Entering the date into post
            post.PublishAt = TempDateOffSet;

           // Entering the tag
            string tempTag = sendInfo.Tags;
            ICollection<string> t=new Collection<string>();
            t.Add(tempTag);
            post.Tags.Add(tempTag);
            

            //if (!ModelState.IsValid)             ////////////////////
            //    return View("Edit", input);    //////////////////

            // Be able to record the user making the actual post
            var user = RavenSession.GetCurrentUser();
            if (string.IsNullOrEmpty(post.AuthorId))
            {
                post.AuthorId = user.Id;
            }
            else
            {
                post.LastEditedByUserId = user.Id;
                post.LastEditedAt = DateTimeOffset.Now;
            }

            if (post.PublishAt <= DateTimeOffset.Now)
           
            {
                var postScheduleringStrategy = new PostSchedulingStrategy(RavenSession, DateTimeOffset.Now);
                post.PublishAt = postScheduleringStrategy.Schedule();
                post.PublishAt = DateTimeOffset.Now;
                post.CreatedAt = DateTimeOffset.Now;
            }

            // Actually save the post now
            RavenSession.Store(post);

            //if (input.IsNewPost())    //////////////////////
            //{
                // Create the post comments object and link between it and the post
                var comments = new PostComments
                               {
                                   Comments = new List<PostComments.Comment>(),
                                   Spam = new List<PostComments.Comment>(),
                                   Post = new PostComments.PostReference
                                          {
                                              Id = post.Id,
                                              PublishAt = post.PublishAt,
                                          }
                               };

                RavenSession.Store(comments);
                post.CommentsId = comments.Id;
            //}                   ////////////////

            return RedirectToAction("Details", new { Id = post.MapTo<PostReference>().DomainId });
        }

        public ActionResult Details(int id)
        {
            var post = RavenSession
                .Include<Post>(x => x.CommentsId)
                .Load(id);

            if (post == null)
                return HttpNotFound();

            var comments = RavenSession.Load<PostComments>(post.CommentsId);

            var vm = new AdminPostDetailsViewModel
                     {
                         Post = post.MapTo<AdminPostDetailsViewModel.PostDetails>(),

                         Comments = comments.Comments
                             .Concat(comments.Spam)
                             .OrderBy(comment => comment.CreatedAt)
                             .MapTo<AdminPostDetailsViewModel.Comment>(),

                         NextPost = RavenSession.GetNextPrevPost(post, true),
                         PreviousPost = RavenSession.GetNextPrevPost(post, false),
                         AreCommentsClosed = comments.AreCommentsClosed(post, BlogConfig.NumberOfDayToCloseComments),
                     };

            return View("Details", vm);
        }

        public ActionResult ListFeed(long start, long end)
        {
            var startAsDateTimeOffset = DateTimeOffsetUtil.ConvertFromUnixTimestamp(start);
            var endAsDateTimeOffset = DateTimeOffsetUtil.ConvertFromUnixTimestamp(end);

            var posts = RavenSession.Query<Post>()
                .Where(post => post.IsDeleted == false)
                .Where
                (
                    post => post.PublishAt >= startAsDateTimeOffset &&
                            post.PublishAt <= endAsDateTimeOffset
                )
                .OrderBy(post => post.PublishAt)
                .Take(256)
                .ToList();

            return Json(posts.MapTo<PostSummaryJson>());
        }


        [HttpPost]
        [AjaxOnly]
        public ActionResult SetPostDate(int id, long date)
        {
            var post = RavenSession
                .Include<Post>(x => x.CommentsId)
                .Load(id);
            if (post == null)
                return Json(new { success = false });

            post.PublishAt = post.PublishAt.WithDate(DateTimeOffsetUtil.ConvertFromJsTimestamp(date));
            RavenSession.Load<PostComments>(post.CommentsId).Post.PublishAt = post.PublishAt;

            return Json(new { success = true });
        }

        [HttpPost]
        public ActionResult CommentsAdmin(int id, CommentCommandOptions command, int[] commentIds)
        {
            if (commentIds == null || commentIds.Length == 0)
                ModelState.AddModelError("CommentIdsAreEmpty", "Not comments was selected.");

            var post = RavenSession.Load<Post>(id);
            if (post == null)
                return HttpNotFound();

            if (ModelState.IsValid == false)
            {
                if (Request.IsAjaxRequest())
                    return Json(new { Success = false, message = ModelState.FirstErrorMessage() });

                return Details(id);
            }

            var comments = RavenSession.Load<PostComments>(post.CommentsId);
            switch (command)
            {
                case CommentCommandOptions.Delete:
                    comments.Comments.RemoveAll(c => commentIds.Contains(c.Id));
                    comments.Spam.RemoveAll(c => commentIds.Contains(c.Id));
                    break;

                case CommentCommandOptions.MarkSpam:
                    var spams = comments.Comments.Concat(comments.Spam)
                        .Where(c => commentIds.Contains(c.Id))
                        .ToArray();

                    comments.Comments.RemoveAll(spams.Contains);
                    comments.Spam.RemoveAll(spams.Contains);
                    foreach (var comment in spams)
                    {
                        AkismetService.MarkSpam(comment);
                    }
                    break;

                case CommentCommandOptions.MarkHam:
                    var ham = comments.Spam
                        .Where(c => commentIds.Contains(c.Id))
                        .ToArray();

                    comments.Spam.RemoveAll(ham.Contains);
                    comments.Comments.AddRange(ham);

                    comments.Comments
                        .Where(c => c.IsSpam)
                        .ForEach(comment =>
                                    {
                                        comment.IsSpam = false;
                                        AkismetService.MarkHam(comment);
                                        ResetNumberOfSpamComments(comment);
                                    });
                    break;
                default:
                    throw new InvalidOperationException(command + " command is not recognized.");
            }

            post.CommentsCount = comments.Comments.Count;

            if (Request.IsAjaxRequest())
            {
                return Json(new { Success = true });
            }
            return RedirectToAction("Details", new { id });
        }

        private void ResetNumberOfSpamComments(PostComments.Comment comment)
        {
            if (comment.CommenterId == null)
                return;
            var commenter = RavenSession.Load<Commenter>(comment.CommenterId);
            if (commenter == null)
                return;
            commenter.NumberOfSpamComments = 0;
        }

        [HttpPost]
        public ActionResult Delete(int id)
        {
            var post = RavenSession.Load<Post>(id);
            post.IsDeleted = true;

            if (Request.IsAjaxRequest())
            {
                return Json(new { Success = true });
            }
            return RedirectToAction("Index");
        }

        [HttpGet]
        public ActionResult DeleteAllSpamComments()
        {
            return View();
        }

        [HttpPost]
        public ActionResult DeleteAllSpamComments(bool deleteAll)
        {
            var patchCommands = RavenSession.Query<PostComments>()
                .Where(x => x.Spam.Count > 0)
                .ToList()
                .Select(x => new PatchCommandData
                             {
                                 Key = x.Id,
                                 Patches = new[]
				             	          {
				             	          	new PatchRequest
				             	          	{
				             	          		Type = PatchCommandType.Set,
				             	          		Name = "Spam",
				             	          		Value = new RavenJArray(),
				             	          	},
				             	          }
                             });

            RavenSession.Advanced.DatabaseCommands.Batch(patchCommands);

            return View();
        }

    }

    public enum CommentCommandOptions
    {
        Delete,
        MarkHam,
        MarkSpam
    }
}