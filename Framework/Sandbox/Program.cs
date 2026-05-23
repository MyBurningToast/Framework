using Framework.Platform;

using var app = new App(new AppConfig
{
    Title = "Sandbox",
    Width = 1280,
    Height = 720,
});

app.Run();