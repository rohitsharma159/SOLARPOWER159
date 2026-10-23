# Solarpowers — ASP.NET Core MVC (.NET 8)

The original Solarpowers design has been split into separate MVC routes and Razor views. Shared header, footer, navigation, and Solar Expert chat live in `Views/Shared/_Layout.cshtml`; page-specific UI is rendered in individual Razor views.

## Pages
- `/` — Home (`Views/Home/Index.cshtml`)
- `/shop` — Shop (`Views/Home/Shop.cshtml`)
- `/learn` — Learn (`Views/Home/Learn.cshtml`)
- `/gallery` — Gallery (`Views/Home/Gallery.cshtml`)
- `/contact` — Contact (`Views/Home/Contact.cshtml`)
- `/feedback` — Feedback (`Views/Home/Feedback.cshtml`)
- `/cart` — Cart (`Views/Home/Cart.cshtml`)
- `/admin` — Admin (`Views/Home/Admin.cshtml`)

## Run locally
Requires the .NET 8 SDK.

```bash
dotnet restore
dotnet build
dotnet run --urls http://localhost:5080
```

Open `http://localhost:5080`.

## Notes
- Original visual styles and page UI scripts are retained in `wwwroot/css/site.css` and `wwwroot/js/site.js`.
- Navigation now opens distinct MVC routes instead of switching all pages inside a single SPA view.
- Cart items are kept in browser local storage so they remain available while navigating between pages.
- SQLite database and API endpoints remain in `Program.cs`.
- Configure admin credentials and any AI API key using environment variables or secure deployment settings; do not commit real secrets.
