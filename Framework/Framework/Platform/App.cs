using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Framework.Platform
{
    public class App : IDisposable
    {
        private readonly IWindow _window;

        public App(AppConfig config)
        {
            var options = WindowOptions.DefaultVulkan with
            {
                Title = config.Title,
                Size = new Vector2D<int>(config.Width, config.Height),
            };

            _window = Window.Create(options);
            _window.Load += OnWindowLoad;
            _window.Update += OnWindowUpdate;
            _window.Render += OnWindowRender;
        }

        private void OnWindowLoad() { }
        private void OnWindowUpdate(double delta) { }
        private void OnWindowRender(double delta) { }

        public void Run() => _window.Run();
        public void Dispose() => _window.Dispose();

    }
}
