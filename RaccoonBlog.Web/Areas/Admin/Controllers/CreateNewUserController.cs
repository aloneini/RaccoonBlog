﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;
using RaccoonBlog.Web.Controllers;
using RaccoonBlog.Web.Infrastructure.Common;
using RaccoonBlog.Web.Models;

namespace RaccoonBlog.Web.Areas.Admin.Controllers
{
    public class CreateNewUserController : RaccoonController
    {  //
        // GET: /Admin/CreateNewUser/
        [HttpGet]
        public ActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public ActionResult Post(UserInput SendInfo)
        {
            User newUser = new User();
            newUser.Email = SendInfo.Email;
            newUser.SetPassword(SendInfo.Password);
            RavenSession.Store(newUser);

            return Json(new { success = true });
        }
        public class UserInput
        {
            public string Email { get; set; }
            public string Password { get; set; }
        }


    }
}
