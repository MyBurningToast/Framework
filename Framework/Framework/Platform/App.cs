using Silk.NET.Core;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using System.Runtime.InteropServices;

namespace Framework.Platform
{
    public unsafe class App : IDisposable
    {
        private readonly IWindow _window;

        private Vk _vk;
        private Instance instance;

        public App(AppConfig config)
        {
            var options = WindowOptions.DefaultVulkan with
            {
                Title = config.Title,
                Size = new Vector2D<int>(config.Width, config.Height),
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

        private void OnWindowLoad() { }
        private void OnWindowUpdate(double delta) { }
        private void OnWindowRender(double delta) { }

        public void Run()
        {
            InitWindow();
            InitVulken();
            MainLoop();
            CleanUp();
        }
        public void Dispose() => _window.Dispose();


        public void InitWindow()
        {

        }

        public void InitVulken()
        {
            CreateInstance();
        }

        public void MainLoop() => _window.Run();

        public void CleanUp()
        {
            _vk.DestroyInstance(instance, null);
            _vk.Dispose();
            _window.Dispose();
        }

        private void CreateInstance()
        {
            _vk = Vk.GetApi();
            ApplicationInfo appInfo = new ApplicationInfo()
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = (byte*)Marshal.StringToHGlobalAnsi("Application"),
                ApplicationVersion = new Version32(1, 0, 0),
                PEngineName = (byte*)Marshal.StringToHGlobalAnsi("No Engine"),
                EngineVersion = new Version32(1, 0, 0),
                ApiVersion = Vk.Version12
            };

            InstanceCreateInfo createInfo = new InstanceCreateInfo()
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo
            };

            byte** glfwExtentions = _window.VkSurface!.GetRequiredExtensions(out uint glfwExtensionCount);

            createInfo.EnabledExtensionCount = glfwExtensionCount;
            createInfo.PpEnabledExtensionNames = glfwExtentions;
            createInfo.EnabledLayerCount = 0;

            if (_vk.CreateInstance(in createInfo, null, out instance) != Result.Success)
            {
                throw new Exception("Failed to create instance");
            }

            Marshal.FreeHGlobal((IntPtr)appInfo.PApplicationName);
            Marshal.FreeHGlobal((IntPtr)appInfo.PEngineName);
        }
    }
}
