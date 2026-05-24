using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Windowing;


namespace Framework.Platform
{
    public unsafe class App : IDisposable
    {
        private readonly IWindow _window;

        private Vk _vk = null!;
        private Instance _instance;

        private bool _enableValidationLayers = true;
        private readonly string[] _validationLayers = new string[]
        {
            "VK_LAYER_KHRONOS_validation"
        };

        private ExtDebugUtils _debugUtils = null!;
        private DebugUtilsMessengerEXT _debugMessenger;

        public App(AppConfig config)
        {
            WindowOptions options = WindowOptions.DefaultVulkan with
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
            SetupDebugMessenger();
        }

        public void MainLoop() => _window.Run();

        public void CleanUp()
        {
            if (_enableValidationLayers)
            {
                _debugUtils.DestroyDebugUtilsMessenger(_instance, _debugMessenger, null);
            }

            _vk.DestroyInstance(_instance, null);
            _vk.Dispose();

            _window.Dispose();
        }

        private void CreateInstance()
        {
            _vk = Vk.GetApi();

            if (_enableValidationLayers && !CheckValidationLayerSupport())
            {
                throw new Exception("Validation layers requested, but not available");
            }

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

            string[] extentions = GetRequiredExtensions();
            createInfo.EnabledExtensionCount = (uint)extentions.Length;
            createInfo.PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(extentions);

            if (_enableValidationLayers)
            {
                createInfo.EnabledLayerCount = (uint)_validationLayers.Length;
                createInfo.PpEnabledLayerNames = (byte**)SilkMarshal.StringArrayToPtr(_validationLayers);

                DebugUtilsMessengerCreateInfoEXT debugCreateInfo = new DebugUtilsMessengerCreateInfoEXT();
                PopulateDebugMessengerCreateInfo(ref debugCreateInfo);
                createInfo.PNext = &debugCreateInfo;
            }
            else
            {
                createInfo.EnabledLayerCount = 0;
                createInfo.PNext = null;
            }

            if (_vk.CreateInstance(in createInfo, null, out _instance) != Result.Success)
            {
                throw new Exception("Failed to create instance");
            }

            Marshal.FreeHGlobal((IntPtr)appInfo.PApplicationName);
            Marshal.FreeHGlobal((IntPtr)appInfo.PEngineName);
            SilkMarshal.Free((nint)createInfo.PpEnabledExtensionNames);

            if (_enableValidationLayers)
            {
                SilkMarshal.Free((nint)createInfo.PpEnabledLayerNames);
            }
        }


        private void PopulateDebugMessengerCreateInfo(ref DebugUtilsMessengerCreateInfoEXT createInfo)
        {
            createInfo.SType = StructureType.DebugUtilsMessengerCreateInfoExt;

            createInfo.MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt |
                                         DebugUtilsMessageSeverityFlagsEXT.WarningBitExt |
                                         DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt;

            createInfo.MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
                                     DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt |
                                     DebugUtilsMessageTypeFlagsEXT.ValidationBitExt;

            createInfo.PfnUserCallback = (DebugUtilsMessengerCallbackFunctionEXT)DebugCallback;
        }

        private void SetupDebugMessenger()
        {
            if (!_enableValidationLayers)
                return;

            if (!_vk.TryGetInstanceExtension(_instance, out _debugUtils))
                return;

            DebugUtilsMessengerCreateInfoEXT createInfo = new DebugUtilsMessengerCreateInfoEXT();
            PopulateDebugMessengerCreateInfo(ref createInfo);

            if (_debugUtils.CreateDebugUtilsMessenger(_instance, in createInfo, null, out _debugMessenger) != Result.Success)
            {
                throw new Exception("Failed to set up debug messenger");
            }
        }

        private string[] GetRequiredExtensions()
        {
            byte** glfwExtensions = _window.VkSurface!.GetRequiredExtensions(out uint glfwExtensionCount);
            string[] extensions = SilkMarshal.PtrToStringArray((nint)glfwExtensions, (int)glfwExtensionCount);

            if (_enableValidationLayers)
            {
                return extensions.Append(ExtDebugUtils.ExtensionName).ToArray();
            }

            return extensions;
        }


        private bool CheckValidationLayerSupport()
        {
            uint layerCount = 0;
            _vk.EnumerateInstanceLayerProperties(ref layerCount, null);
            LayerProperties[] availableLayers = new LayerProperties[layerCount];
            fixed (LayerProperties* availableLayersPtr = availableLayers)
            {
                _vk.EnumerateInstanceLayerProperties(ref layerCount, availableLayersPtr);
            }

            HashSet<string> availableLayerNames = new();

            foreach (LayerProperties layer in availableLayers)
            {
                string? name = Marshal.PtrToStringAnsi((IntPtr)layer.LayerName);
                if (name != null)
                    availableLayerNames.Add(name);
            }

            return _validationLayers.All(availableLayerNames.Contains);
        }


        private uint DebugCallback(DebugUtilsMessageSeverityFlagsEXT messageSeverity, DebugUtilsMessageTypeFlagsEXT messageTypes, DebugUtilsMessengerCallbackDataEXT* pCallbackData, void* pUserData)
        {
            Console.WriteLine($"Validation layer:" + Marshal.PtrToStringAnsi((nint)pCallbackData->PMessage));

            return Vk.False;
        }
    }
}
