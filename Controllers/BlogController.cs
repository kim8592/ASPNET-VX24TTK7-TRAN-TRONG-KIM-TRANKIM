using Microsoft.AspNetCore.Mvc;
using MilkTeaWeb.ViewModels.Blog;

namespace MilkTeaWeb.Controllers;

public sealed class BlogController : Controller
{
    [HttpGet]
    public IActionResult Index() => View(BlogPosts.All);

    [HttpGet("/Blog/Details/{slug}")]
    public IActionResult Details(string slug)
    {
        var post = BlogPosts.All.FirstOrDefault(candidate =>
            string.Equals(candidate.Slug, slug, StringComparison.OrdinalIgnoreCase));

        return post is null ? NotFound() : View(post);
    }
}
