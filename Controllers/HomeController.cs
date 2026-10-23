using Microsoft.AspNetCore.Mvc;

namespace SolarPowers.Controllers;

public class HomeController : Controller
{
    [HttpGet("/")]
    public IActionResult Index() => View("Index");
    [HttpGet("/shop")]
    public IActionResult Shop() => View("Shop");
    [HttpGet("/learn")]
    public IActionResult Learn() => View("Learn");
    [HttpGet("/gallery")]
    public IActionResult Gallery() => View("Gallery");
    [HttpGet("/contact")]
    public IActionResult Contact() => View("Contact");
    [HttpGet("/feedback")]
    public IActionResult Feedback() => View("Feedback");
    [HttpGet("/cart")]
    public IActionResult Cart() => View("Cart");
    [HttpGet("/admin")]
    public IActionResult Admin() => View("Admin");
}
