using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;

namespace Framework
{
    public unsafe partial class App : IDisposable
    {
        private readonly AppConfig _config;

        private IWindow _window = null!;
        private Vk _vk = null!;
        private Instance _instance;

        private ExtDebugUtils _debugUtils = null!;
        private DebugUtilsMessengerEXT _debugMessenger;

        private KhrSurface _khrSurface = null!;
        private SurfaceKHR _surface;

        private PhysicalDevice _physicalDevice;
        private Device _device;

        private Queue _graphicsQueue;
        private Queue _presentQueue;

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
            _vk.DestroyDevice(_device, null);

            if (_config.EnableValidationLayers)
            {
                _debugUtils.DestroyDebugUtilsMessenger(_instance, _debugMessenger, null);
            }

            _khrSurface.DestroySurface(_instance, _surface, null);
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
            CreateSurface();
            PickPhysicalDevice();
            CreateLogicalDevice();
        }
    }
}
