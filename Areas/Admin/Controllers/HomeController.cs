using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MilkTeaWeb.Security;

namespace MilkTeaWeb.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class HomeController : Controller
{
    public IActionResult Index() => View();
}
