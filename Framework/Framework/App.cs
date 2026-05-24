using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Windowing;

namespace Framework
{
    public unsafe partial class App : IDisposable
    {
        private readonly AppConfig _config;

        private IWindow _window;
        private Vk _vk;
        private Instance _instance;

        private ExtDebugUtils _debugUtils;
        private DebugUtilsMessengerEXT _debugMessenger;

        private PhysicalDevice _physicalDevice;
        private Device device;

        private Queue graphicsQueue;

        public App(AppConfig config)
        {
            _config = config;
        }

        public void Run()
        {
            InitWindow();
            InitVulkan();
            MainLoop();
            CleanUp();
        }

        public void MainLoop() => _window.Run();
        public void Dispose() => _window.Dispose();

        private void OnWindowLoad() { }
        private void OnWindowUpdate(double delta) { }
        private void OnWindowRender(double delta) { }

        private void CleanUp()
        {
            if (_config.EnableValidationLayers)
            {
                _debugUtils.DestroyDebugUtilsMessenger(_instance, _debugMessenger, null);
            }

            _vk.DestroyDevice(device, null);
            _vk.DestroyInstance(_instance, null);
            _vk.Dispose();
            _window.Dispose();
        }

        private void InitWindow()
        {
            WindowOptions options = WindowOptions.DefaultVulkan with
            {
                Title = _config.Title,
                Size = new Vector2D<int>(_config.Width, _config.Height),
            };

            _window = Window.Create(options);
            _window.Initialize();

            if (_window.VkSurface is null)
            {
                throw new Exception("Windowing platform doesn't support Vulkan");
            }

            _window.Load += OnWindowLoad;
            _window.Update += OnWindowUpdate;
            _window.Render += OnWindowRender;
        }

        private void InitVulkan()
        {
            CreateInstance();
            SetupDebugMessenger();
            PickPhysicalDevice();
            CreateLogicalDevice();
        }
    }
}
