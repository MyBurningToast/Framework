using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Framework
{
	internal struct QueueFamilyIndices
	{
		public uint? GraphicsFamily { get; set; }
		public uint? PresentFamily { get; set; }
		public bool IsComplete()
		{
			return GraphicsFamily.HasValue && PresentFamily.HasValue;
		}
	}

	internal struct SwapChainSupportDetails
	{
		public SurfaceCapabilitiesKHR Capabilities;
		public SurfaceFormatKHR[] Formats;
		public PresentModeKHR[] PresentModes;
	}

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

		private KhrSwapchain _khrSwapChain = null!;
		private SwapchainKHR _swapchain;
		private Image[] _swapChainImages = null!;
		private Format _swapChainImageFormat;
		private Extent2D _swapChainExtent;
		private ImageView[] _swapChainImageViews = null!;
        private Framebuffer[] _swapChainFramebuffers = null!;

        private RenderPass _renderPass;
        private PipelineLayout _pipelineLayout;
		private Pipeline _graphicsPipeline;

		private CommandPool _commandPool;
		private CommandBuffer[] _commandBuffers = null!;

		private Semaphore[] _imageAvailableSemaphores = null!;
		private Semaphore[] _renderFinishedSemaphores = null!;
		private Fence[] _inFlightFences;
		private Fence[] _imagesInFlight;
		private int _currentFrame = 0;

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

		private void MainLoop()
		{
			_window.Render += DrawFrame;
			_window.Run();
			_vk.DeviceWaitIdle(_device);
		}
        public void Dispose() => _window.Dispose();

        private void OnWindowLoad() { }
        private void OnWindowUpdate(double delta) { }
        private void OnWindowRender(double delta) { }

        private void CleanUp()
        {
			for (int i = 0; i < _config.MaxFramesInFlight; i++)
			{
                _vk.DestroySemaphore(_device, _renderFinishedSemaphores[i], null);
                _vk.DestroySemaphore(_device, _imageAvailableSemaphores[i], null);
                _vk.DestroyFence(_device, _inFlightFences[i], null);
            }

			_vk.DestroyCommandPool(_device, _commandPool, null);

			foreach (var frameBuffer in _swapChainFramebuffers)
			{
				_vk.DestroyFramebuffer(_device, frameBuffer, null);
			}

			_vk.DestroyPipeline(_device, _graphicsPipeline, null);
			_vk.DestroyPipelineLayout(_device, _pipelineLayout, null);
			_vk.DestroyRenderPass(_device, _renderPass, null);

			foreach (var imageView in _swapChainImageViews)
			{
				_vk.DestroyImageView(_device, imageView, null);
			}

            _khrSwapChain.DestroySwapchain(_device, _swapchain, null);
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

			CreateSwapChain();
			CreateImageViews();
			CreateRenderPass();
            CreateGraphicsPipeline();
			CreateFramebuffers();

			CreateCommandPool();
			CreateCommandBuffers();

			CreateSyncObjects();
		}


        private void CreateSwapChain()
        {
            var swapChainSupport = QuerySwapChainSupport(_physicalDevice);

            var surfaceFormat = ChooseSwapSurfaceFormat(swapChainSupport.Formats);
            var presentMode = ChoosePresentMode(swapChainSupport.PresentModes);
            var extent = ChooseSwapExtent(swapChainSupport.Capabilities);

            var imageCount = swapChainSupport.Capabilities.MinImageCount + 1;
            var maxImageCount = swapChainSupport.Capabilities.MaxImageCount;
            if (imageCount > maxImageCount && maxImageCount > 0)
            {
                imageCount = maxImageCount;
            }

            SwapchainCreateInfoKHR createInfo = new()
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = _surface,

                MinImageCount = imageCount,
                ImageFormat = surfaceFormat.Format,
                ImageColorSpace = surfaceFormat.ColorSpace,
                ImageExtent = extent,
                ImageArrayLayers = 1,
                ImageUsage = ImageUsageFlags.ColorAttachmentBit
            };

            var indices = FindQueueFamilies(_physicalDevice);
            var queueFamilyIndices = stackalloc[]
            {
                indices.GraphicsFamily!.Value,
                indices.PresentFamily!.Value
            };

            if (indices.GraphicsFamily != indices.PresentFamily)
            {
                createInfo = createInfo with
                {
                    ImageSharingMode = SharingMode.Concurrent,
                    QueueFamilyIndexCount = 2,
                    PQueueFamilyIndices = queueFamilyIndices
                };
            }
            else
            {
                createInfo.ImageSharingMode = SharingMode.Exclusive;
            }

            createInfo = createInfo with
            {
                PreTransform = swapChainSupport.Capabilities.CurrentTransform,
                CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
                PresentMode = presentMode,
                Clipped = true,

                OldSwapchain = default
            };

            if (!_vk.TryGetDeviceExtension(_instance,_device, out _khrSwapChain))
            {
                throw new NotSupportedException("VK_KHR_swapchain extension not found");
            }

            if (_khrSwapChain.CreateSwapchain(_device, in createInfo, null, out _swapchain) != Result.Success)
            {
                throw new Exception("Failed to create swap chain");
            }

            _khrSwapChain.GetSwapchainImages(_device, _swapchain, ref imageCount, null);
            _swapChainImages = new Image[imageCount];
            fixed (Image* swapChainImagesPtr = _swapChainImages)
            {
                _khrSwapChain.GetSwapchainImages(_device, _swapchain, ref imageCount, swapChainImagesPtr);
            }

            _swapChainImageFormat = surfaceFormat.Format;
            _swapChainExtent = extent;
        }

        private SurfaceFormatKHR ChooseSwapSurfaceFormat(IReadOnlyList<SurfaceFormatKHR> availableFormats)
        {
            foreach (var availableFormat in availableFormats)
            {
                if (availableFormat.Format == Format.B8G8R8A8Srgb && availableFormat.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
                {
                    return availableFormat;
                }
            }

            return availableFormats[0];
        }

        private PresentModeKHR ChoosePresentMode(IReadOnlyList<PresentModeKHR> availablePresentModes) 
        {
            foreach (var availablePresentMode in availablePresentModes)
            {
                if (availablePresentMode == PresentModeKHR.MailboxKhr)
                {
                    return availablePresentMode;
                }
            }

            return PresentModeKHR.FifoKhr;
        }
        private Extent2D ChooseSwapExtent(SurfaceCapabilitiesKHR capabilities) 
        {
            if (capabilities.CurrentExtent.Width != uint.MaxValue)
            {
                return capabilities.CurrentExtent;
            }
            else
            {
                var frameBufferSize = _window.FramebufferSize;

                Extent2D actualExtent = new()
                {
                    Width = (uint)frameBufferSize.X,
                    Height = (uint)frameBufferSize.Y,
                };

                actualExtent.Width = Math.Clamp(actualExtent.Width, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width);
                actualExtent.Height = Math.Clamp(actualExtent.Height, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height);
                return actualExtent;
            }
        }
        private SwapChainSupportDetails QuerySwapChainSupport(PhysicalDevice physicalDevice) 
        {
            var details = new SwapChainSupportDetails();

            _khrSurface.GetPhysicalDeviceSurfaceCapabilities(physicalDevice, _surface, out details.Capabilities);

            uint formatCount = 0;
            _khrSurface.GetPhysicalDeviceSurfaceFormats(physicalDevice, _surface, ref formatCount, null);

            if (formatCount != 0)
            {
                details.Formats = new SurfaceFormatKHR[formatCount];
                fixed (SurfaceFormatKHR* formatPtr = details.Formats)
                {
                    _khrSurface.GetPhysicalDeviceSurfaceFormats(physicalDevice, _surface, ref formatCount, formatPtr);
                }
            }
            else
            {
                details.Formats = Array.Empty<SurfaceFormatKHR>();
            }

            uint presentModeCount = 0;
            _khrSurface.GetPhysicalDeviceSurfacePresentModes(physicalDevice, _surface, ref presentModeCount, null);

            if (presentModeCount != 0)
            {
                details.PresentModes = new PresentModeKHR[presentModeCount];
                fixed (PresentModeKHR* formatsPtr = details.PresentModes)
                {
                    _khrSurface.GetPhysicalDeviceSurfacePresentModes(physicalDevice, _surface, ref presentModeCount, formatsPtr);
                }
            }
            else
            {
                details.PresentModes = Array.Empty<PresentModeKHR>();
            }

            return details;

        }

		private void CreateInstance()
		{
			_vk = Vk.GetApi();

			if (_config.EnableValidationLayers && !CheckValidationLayerSupport())
				throw new Exception("Validation layers requested, but not available");

			ApplicationInfo appInfo = new()
			{
				SType = StructureType.ApplicationInfo,
				PApplicationName = (byte*)Marshal.StringToHGlobalAnsi("Application"),
				ApplicationVersion = new Version32(1, 0, 0),
				PEngineName = (byte*)Marshal.StringToHGlobalAnsi("No Engine"),
				EngineVersion = new Version32(1, 0, 0),
				ApiVersion = Vk.Version12
			};

			InstanceCreateInfo createInfo = new()
			{
				SType = StructureType.InstanceCreateInfo,
				PApplicationInfo = &appInfo
			};

			string[] extensions = GetRequiredExtensions();
			createInfo.EnabledExtensionCount = (uint)extensions.Length;
			createInfo.PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(extensions);

			if (_config.EnableValidationLayers)
			{
				createInfo.EnabledLayerCount = (uint)_config.ValidationLayers.Length;
				createInfo.PpEnabledLayerNames = (byte**)SilkMarshal.StringArrayToPtr(_config.ValidationLayers);

				DebugUtilsMessengerCreateInfoEXT debugCreateInfo = new();
				PopulateDebugMessengerCreateInfo(ref debugCreateInfo);
				createInfo.PNext = &debugCreateInfo;
			}
			else
			{
				createInfo.EnabledLayerCount = 0;
				createInfo.PNext = null;
			}

			if (_vk.CreateInstance(in createInfo, null, out _instance) != Result.Success)
				throw new Exception("Failed to create instance");

			Marshal.FreeHGlobal((nint)appInfo.PApplicationName);
			Marshal.FreeHGlobal((nint)appInfo.PEngineName);
			SilkMarshal.Free((nint)createInfo.PpEnabledExtensionNames);

			if (_config.EnableValidationLayers)
				SilkMarshal.Free((nint)createInfo.PpEnabledLayerNames);
		}

		private void PopulateDebugMessengerCreateInfo(ref DebugUtilsMessengerCreateInfoEXT createInfo)
		{
			createInfo.SType = StructureType.DebugUtilsMessengerCreateInfoExt;
			createInfo.MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt | DebugUtilsMessageSeverityFlagsEXT.WarningBitExt | DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt;
			createInfo.MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt;
			createInfo.PfnUserCallback = (DebugUtilsMessengerCallbackFunctionEXT)DebugCallback;
		}

		private void SetupDebugMessenger()
		{
			if (!_config.EnableValidationLayers) return;
			if (!_vk.TryGetInstanceExtension(_instance, out _debugUtils)) return;

			DebugUtilsMessengerCreateInfoEXT createInfo = new();
			PopulateDebugMessengerCreateInfo(ref createInfo);

			if (_debugUtils.CreateDebugUtilsMessenger(_instance, in createInfo, null, out _debugMessenger) != Result.Success)
				throw new Exception("Failed to set up debug messenger");
		}

		private uint DebugCallback(DebugUtilsMessageSeverityFlagsEXT messageSeverity, DebugUtilsMessageTypeFlagsEXT messageTypes, DebugUtilsMessengerCallbackDataEXT* pCallbackData, void* pUserData)
		{
			Console.WriteLine("Validation layer: " + Marshal.PtrToStringAnsi((nint)pCallbackData->PMessage));
			return Vk.False;
		}

		private void PickPhysicalDevice()
		{
			IReadOnlyCollection<PhysicalDevice> devices = _vk.GetPhysicalDevices(_instance);

			foreach (var device in devices)
			{
				if (IsDeviceSuitable(device))
				{
					_physicalDevice = device;
					break;
				}
			}

			if (_physicalDevice.Handle == 0)
				throw new Exception("Failed to find a suitable GPU");
		}

		private bool IsDeviceSuitable(PhysicalDevice device)
		{
			var indices = FindQueueFamilies(device);
			bool extensionsSupported = CheckDeviceExtensionsSupport(device);

			bool swapChainAdequate = false;
			if (extensionsSupported)
			{
				var swapChainSupport = QuerySwapChainSupport(device);
				swapChainAdequate = swapChainSupport.Formats.Any() && swapChainSupport.PresentModes.Any();
			}

			return indices.IsComplete() && extensionsSupported && swapChainAdequate;
		}

		private bool CheckDeviceExtensionsSupport(PhysicalDevice device)
		{
			uint count = 0;
			_vk.EnumerateDeviceExtensionProperties(device, (byte*)null, ref count, null);
			var available = new ExtensionProperties[count];
			fixed (ExtensionProperties* ptr = available)
				_vk.EnumerateDeviceExtensionProperties(device, (byte*)null, ref count, ptr);

			var names = available.Select(e => Marshal.PtrToStringAnsi((nint)e.ExtensionName)).ToHashSet();
			return _config.DeviceExtensions.All(names.Contains);
		}

		private QueueFamilyIndices FindQueueFamilies(PhysicalDevice device)
		{
			var indices = new QueueFamilyIndices();

			uint queueFamilityCount = 0;
			_vk.GetPhysicalDeviceQueueFamilyProperties(device, ref queueFamilityCount, null);

			var queueFamilies = new QueueFamilyProperties[queueFamilityCount];
			fixed (QueueFamilyProperties* queueFamiliesPtr = queueFamilies)
			{
				_vk.GetPhysicalDeviceQueueFamilyProperties(device, ref queueFamilityCount, queueFamiliesPtr);
			}

			uint i = 0;
			foreach (var queueFamily in queueFamilies)
			{
				if (queueFamily.QueueFlags.HasFlag(QueueFlags.GraphicsBit))
					indices.GraphicsFamily = i;

				_khrSurface.GetPhysicalDeviceSurfaceSupport(device, i, _surface, out var presentSupport);

				if (presentSupport)
					indices.PresentFamily = i;

				if (indices.IsComplete())
					break;
				i++;
			}

			return indices;
		}
		private string[] GetRequiredExtensions()
		{
			byte** glfwExtensions = _window.VkSurface!.GetRequiredExtensions(out uint glfwExtensionCount);
			string[] extensions = SilkMarshal.PtrToStringArray((nint)glfwExtensions, (int)glfwExtensionCount);

			if (_config.EnableValidationLayers)
				return extensions.Append(ExtDebugUtils.ExtensionName).ToArray();

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

			var availableLayerNames = availableLayers.Select(layer => Marshal.PtrToStringAnsi((nint)layer.LayerName)).ToHashSet();

			return _config.ValidationLayers.All(availableLayerNames.Contains);
		}

		private void CreateLogicalDevice()
		{
			var indices = FindQueueFamilies(_physicalDevice);

			var uniqueQueueFamilies = new[] { indices.GraphicsFamily!.Value, indices.PresentFamily!.Value }.Distinct().ToArray();

			using var mem = GlobalMemory.Allocate(uniqueQueueFamilies.Length * sizeof(DeviceQueueCreateInfo));
			var queueCreateInfos = (DeviceQueueCreateInfo*)Unsafe.AsPointer(ref mem.GetPinnableReference());

			float queuePriority = 1f;
			for (int i = 0; i < uniqueQueueFamilies.Length; i++)
			{
				queueCreateInfos[i] = new()
				{
					SType = StructureType.DeviceQueueCreateInfo,
					QueueFamilyIndex = uniqueQueueFamilies[i],
					QueueCount = 1,
					PQueuePriorities = &queuePriority
				};
			}

			PhysicalDeviceFeatures deviceFeatures = new();
			DeviceCreateInfo createInfo = new()
			{
				SType = StructureType.DeviceCreateInfo,
				QueueCreateInfoCount = (uint)uniqueQueueFamilies.Length,
				PQueueCreateInfos = queueCreateInfos,
				PEnabledFeatures = &deviceFeatures,
				EnabledExtensionCount = (uint)_config.DeviceExtensions.Length,
				PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(_config.DeviceExtensions)
			};

            createInfo.EnabledLayerCount = 0;
            createInfo.PpEnabledLayerNames = null;

            if (_vk.CreateDevice(_physicalDevice, in createInfo, null, out _device) != Result.Success)
			{
				throw new Exception("Failed to create logical device");
			}

			_vk.GetDeviceQueue(_device, indices.GraphicsFamily!.Value, 0, out _graphicsQueue);
			_vk.GetDeviceQueue(_device, indices.PresentFamily!.Value, 0, out _presentQueue);

			if (_config.EnableValidationLayers)
			{
				SilkMarshal.Free((nint)createInfo.PpEnabledLayerNames);
			}
		}

		private void CreateSurface()
		{
			if (!_vk.TryGetInstanceExtension<KhrSurface>(_instance, out _khrSurface))
			{
				throw new NotSupportedException("KHR_surface extension not found");
			}

			_surface = _window.VkSurface!.Create<AllocationCallbacks>(_instance.ToHandle(), null).ToSurface();
		}

		private void CreateImageViews()
		{
			_swapChainImageViews = new ImageView[_swapChainImages.Length];

			for (int i = 0; i < _swapChainImages.Length; i++)
			{
				ImageViewCreateInfo createInfo = new()
				{
					SType = StructureType.ImageViewCreateInfo,
					Image = _swapChainImages[i],
					ViewType = ImageViewType.Type2D,
					Format = _swapChainImageFormat,

					Components =
					{
						R = ComponentSwizzle.Identity,
						G = ComponentSwizzle.Identity,
						B = ComponentSwizzle.Identity,
						A = ComponentSwizzle.Identity,
					},

					SubresourceRange =
					{
						AspectMask = ImageAspectFlags.ColorBit,
						BaseMipLevel = 0,
						LevelCount = 1,
						BaseArrayLayer = 0,
						LayerCount = 1,
					}
				};

				if (_vk.CreateImageView(_device, in createInfo, null, out _swapChainImageViews[i]) != Result.Success)
				{
					throw new Exception("Failed to create image views");
				}
			}
		}

		private void CreateRenderPass()
		{
            AttachmentDescription colorAttachment = new()
            {
                Format = _swapChainImageFormat,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.PresentSrcKhr,
            };

            AttachmentReference colorAttachmentRef = new()
            {
                Attachment = 0,
                Layout = ImageLayout.ColorAttachmentOptimal,
            };

            SubpassDescription subpass = new()
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = 1,
                PColorAttachments = &colorAttachmentRef,
            };

			SubpassDependency dependency = new()
			{
				SrcSubpass = Vk.SubpassExternal,
				DstSubpass = 0,
				SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
				SrcAccessMask = 0,
				DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
				DstAccessMask = AccessFlags.ColorAttachmentWriteBit
			};

            RenderPassCreateInfo renderPassInfo = new()
            {
                SType = StructureType.RenderPassCreateInfo,
                AttachmentCount = 1,
                PAttachments = &colorAttachment,
                SubpassCount = 1,
                PSubpasses = &subpass,

				DependencyCount = 1,
				PDependencies = &dependency
            };

            if (_vk.CreateRenderPass(_device, in renderPassInfo, null, out _renderPass) != Result.Success)
            {
                throw new Exception("failed to create render pass!");
            }
        }

        private void CreateFramebuffers()
        {
            _swapChainFramebuffers = new Framebuffer[_swapChainImageViews.Length];

            for (int i = 0; i < _swapChainImageViews.Length; i++)
            {
                var attachment = _swapChainImageViews[i];

                FramebufferCreateInfo framebufferInfo = new()
                {
                    SType = StructureType.FramebufferCreateInfo,
                    RenderPass = _renderPass,
                    AttachmentCount = 1,
                    PAttachments = &attachment,
                    Width = _swapChainExtent.Width,
                    Height = _swapChainExtent.Height,
                    Layers = 1,
                };

                if (_vk.CreateFramebuffer(_device, in framebufferInfo, null, out _swapChainFramebuffers[i]) != Result.Success)
                {
                    throw new Exception("failed to create framebuffer!");
                }
            }
        }

        private void CreateGraphicsPipeline()
		{
            var vertShadeCode = File.ReadAllBytes(@"..\..\..\..\Framework\Shaders\vert.spv");
            var fragShaderCode = File.ReadAllBytes(@"..\..\..\..\Framework\Shaders\frag.spv");

			var vertShaderModule = CreateShaderModule(vertShadeCode);
			var fragShaderModule = CreateShaderModule(fragShaderCode);

			PipelineShaderStageCreateInfo vertShaderStageInfo = new()
			{
				SType = StructureType.PipelineShaderStageCreateInfo,
				Stage = ShaderStageFlags.VertexBit,
				Module = vertShaderModule,
				PName = (byte*)SilkMarshal.StringToPtr("main")
			};

			PipelineShaderStageCreateInfo fragShaderStageInfo = new()
			{
				SType = StructureType.PipelineShaderStageCreateInfo,
				Stage = ShaderStageFlags.FragmentBit,
				Module = fragShaderModule,
				PName = (byte*)SilkMarshal.StringToPtr("main")
			};

			var shaderStages = stackalloc[]
			{
				vertShaderStageInfo,
				fragShaderStageInfo
			};

			PipelineVertexInputStateCreateInfo vertexInputInfo = new()
			{
				SType = StructureType.PipelineVertexInputStateCreateInfo,
				VertexBindingDescriptionCount = 0,
				VertexAttributeDescriptionCount = 0
			};
			PipelineInputAssemblyStateCreateInfo inputAssemblyInfo = new()
			{
				SType = StructureType.PipelineInputAssemblyStateCreateInfo,
				Topology = PrimitiveTopology.TriangleList,
				PrimitiveRestartEnable = false
			};

			Viewport viewport = new()
			{
				X = 0,
				Y = 0,
				Width = _swapChainExtent.Width,
				Height = _swapChainExtent.Height,
				MinDepth = 0,
				MaxDepth = 1	
			};
			
			Rect2D scissor = new()
			{
				Offset = { X = 0, Y = 0 },
				Extent = _swapChainExtent
			};

			PipelineViewportStateCreateInfo viewportInfo = new()
			{
				SType = StructureType.PipelineViewportStateCreateInfo,
				ViewportCount = 1,
				PViewports = &viewport,
				ScissorCount = 1,
				PScissors = &scissor
			};

			PipelineRasterizationStateCreateInfo rasterizerInfo = new()
			{
				SType = StructureType.PipelineRasterizationStateCreateInfo,
				DepthClampEnable = false,
				RasterizerDiscardEnable = false,
				PolygonMode = PolygonMode.Fill,
				LineWidth = 1,
				CullMode = CullModeFlags.BackBit,
				FrontFace = FrontFace.Clockwise,
				DepthBiasEnable = false
			};

			PipelineMultisampleStateCreateInfo multisamplingInfo = new()
			{
				SType = StructureType.PipelineMultisampleStateCreateInfo,
				SampleShadingEnable = false,
				RasterizationSamples = SampleCountFlags.Count1Bit
			};

			PipelineColorBlendAttachmentState colorBlendAttachment = new()
			{
				ColorWriteMask =
					ColorComponentFlags.RBit |
					ColorComponentFlags.GBit |
					ColorComponentFlags.BBit |
					ColorComponentFlags.ABit,
				BlendEnable = false
			};

			PipelineColorBlendStateCreateInfo colorBlendInfo = new()
			{
				SType = StructureType.PipelineColorBlendStateCreateInfo,
				LogicOpEnable = false,
				LogicOp = LogicOp.Copy,
				AttachmentCount = 1,
				PAttachments = &colorBlendAttachment
			};

			colorBlendInfo.BlendConstants[0] = 0;
			colorBlendInfo.BlendConstants[1] = 0;
			colorBlendInfo.BlendConstants[2] = 0;
			colorBlendInfo.BlendConstants[3] = 0;

			PipelineLayoutCreateInfo pipelineLayoutInfo = new()
			{
				SType = StructureType.PipelineLayoutCreateInfo,
				SetLayoutCount = 0,
				PushConstantRangeCount = 0
			};

			if (_vk.CreatePipelineLayout(_device, in pipelineLayoutInfo, null, out _pipelineLayout) != Result.Success)
			{
				throw new Exception("Failed to create pipeline layout");
			}

			GraphicsPipelineCreateInfo pipelineInfo = new()
			{
				SType = StructureType.GraphicsPipelineCreateInfo,

				StageCount = 2,
				PStages = shaderStages,

				PVertexInputState = &vertexInputInfo,
				PInputAssemblyState = &inputAssemblyInfo,
				PViewportState = &viewportInfo,
				PRasterizationState = &rasterizerInfo,
				PMultisampleState = &multisamplingInfo,
				PColorBlendState = &colorBlendInfo,

				Layout = _pipelineLayout,
				RenderPass = _renderPass,
				Subpass = 0,

				BasePipelineHandle = default,
				BasePipelineIndex = -1,
			};

			if (_vk.CreateGraphicsPipelines(_device, default, 1, in pipelineInfo, null, out _graphicsPipeline) != Result.Success)
				throw new Exception("Failed to create graphics pipeline");

            _vk.DestroyShaderModule(_device, fragShaderModule, null);
			_vk.DestroyShaderModule(_device, vertShaderModule, null);

			SilkMarshal.Free((nint)vertShaderStageInfo.PName);
			SilkMarshal.Free((nint)fragShaderStageInfo.PName);
		}

		private ShaderModule CreateShaderModule(byte[] code)
		{
			ShaderModuleCreateInfo createInfo = new()
			{
				SType = StructureType.ShaderModuleCreateInfo,
				CodeSize = (nuint)code.Length,
			};

			ShaderModule shaderModule;
			fixed (byte* codePtr = code)
			{
				createInfo.PCode = (uint*)codePtr;
				if (_vk.CreateShaderModule(_device, in createInfo, null, out shaderModule) != Result.Success)
					throw new Exception();
			}

			return shaderModule;
		}

		private void CreateCommandPool()
		{
			var queueFamiliyIndicies = FindQueueFamilies(_physicalDevice);

			CommandPoolCreateInfo poolInfo = new()
			{
				SType = StructureType.CommandPoolCreateInfo,
				QueueFamilyIndex = queueFamiliyIndicies.GraphicsFamily!.Value
			};

			if (_vk.CreateCommandPool(_device, in poolInfo, null, out _commandPool) != Result.Success)
				throw new Exception("Failed to create command pool");
        }

		private void CreateCommandBuffers()
		{
			_commandBuffers = new CommandBuffer[_swapChainFramebuffers.Length];
			CommandBufferAllocateInfo allocateInfo = new()
			{
				SType = StructureType.CommandBufferAllocateInfo,
				CommandPool = _commandPool,
				Level = CommandBufferLevel.Primary,
				CommandBufferCount = (uint)_commandBuffers.Length
			};

			fixed (CommandBuffer* commandBufferPtr = _commandBuffers)
			{
				if (_vk.AllocateCommandBuffers(_device, in allocateInfo, commandBufferPtr) != Result.Success)
					throw new Exception("Failed to allocate command buffers");
			}

			for (int i = 0; i < _commandBuffers.Length; i++)
			{
				CommandBufferBeginInfo beginInfo = new()
				{
					SType = StructureType.CommandBufferBeginInfo,
				};

				if (_vk.BeginCommandBuffer(_commandBuffers[i], in beginInfo) != Result.Success)
					throw new Exception("Failed to begin recording command buffer");

				RenderPassBeginInfo renderPassBeginInfo = new()
				{
					SType = StructureType.RenderPassBeginInfo,
					RenderPass = _renderPass,
					Framebuffer = _swapChainFramebuffers[i],
					RenderArea =
					{
						Offset = { X = 0, Y = 0 },
						Extent = _swapChainExtent
					}
				};

                ClearValue clearColor = new()
                {
                    Color = new() { Float32_0 = 0, Float32_1 = 0, Float32_2 = 0, Float32_3 = 1 },
                };

				renderPassBeginInfo.ClearValueCount = 1;
				renderPassBeginInfo.PClearValues = &clearColor;

				_vk.CmdBeginRenderPass(_commandBuffers[i], &renderPassBeginInfo, SubpassContents.Inline);
				_vk.CmdBindPipeline(_commandBuffers[i], PipelineBindPoint.Graphics, _graphicsPipeline);

				_vk.CmdDraw(_commandBuffers[i], 3, 1, 0, 0);
				_vk.CmdEndRenderPass(_commandBuffers[i]);

				if (_vk.EndCommandBuffer(_commandBuffers[i]) != Result.Success)
					throw new Exception("Failed to record command buffer");
            }
		}

		private void CreateSyncObjects()
		{
			_imageAvailableSemaphores = new Semaphore[_config.MaxFramesInFlight];
			_renderFinishedSemaphores = new Semaphore[_config.MaxFramesInFlight];
			_inFlightFences = new Fence[_config.MaxFramesInFlight];
			_imagesInFlight = new Fence[_swapChainImages.Length];

			SemaphoreCreateInfo semaphoreInfo = new()
			{
				SType = StructureType.SemaphoreCreateInfo
			};

			FenceCreateInfo fenceInfo = new()
			{
				SType = StructureType.FenceCreateInfo,
				Flags = FenceCreateFlags.SignaledBit
			};

			for (var i = 0; i < _config.MaxFramesInFlight; i++)
			{
				if (_vk!.CreateSemaphore(_device, in semaphoreInfo, null, out _imageAvailableSemaphores[i]) != Result.Success ||
					_vk!.CreateSemaphore(_device, in semaphoreInfo, null, out _renderFinishedSemaphores[i]) != Result.Success ||
					_vk!.CreateFence(_device, in fenceInfo, null, out _inFlightFences[i]) != Result.Success)
				{
					throw new Exception("Failed to create synchronization objects for a frame");
				}
			}
		}

		private void DrawFrame(double delta)
		{
            _vk.WaitForFences(_device, 1, in _inFlightFences[_currentFrame], true, ulong.MaxValue);

            uint imageIndex = 0;
            _khrSwapChain.AcquireNextImage(_device, _swapchain, ulong.MaxValue, _imageAvailableSemaphores[_currentFrame], default, ref imageIndex);

            if (_imagesInFlight[imageIndex].Handle != default)
            {
                _vk.WaitForFences(_device, 1, in _inFlightFences[_currentFrame], true, ulong.MaxValue);
            }
            _imagesInFlight[imageIndex] = _inFlightFences[_currentFrame];

            SubmitInfo submitInfo = new()
            {
                SType = StructureType.SubmitInfo,
            };

            var waitSemaphores = stackalloc[]
			{
				_imageAvailableSemaphores[_currentFrame]
			};
            var waitStages = stackalloc[]
			{
				PipelineStageFlags.ColorAttachmentOutputBit
			};

            var buffer = _commandBuffers[imageIndex];

            submitInfo = submitInfo with
            {
                WaitSemaphoreCount = 1,
                PWaitSemaphores = waitSemaphores,
                PWaitDstStageMask = waitStages,

                CommandBufferCount = 1,
                PCommandBuffers = &buffer
            };

            var signalSemaphores = stackalloc[]
			{
				_renderFinishedSemaphores[_currentFrame]
			};

            submitInfo = submitInfo with
            {
                SignalSemaphoreCount = 1,
                PSignalSemaphores = signalSemaphores,
            };

            _vk.ResetFences(_device, 1, in _inFlightFences[_currentFrame]);

            if (_vk.QueueSubmit(_graphicsQueue, 1, in submitInfo, _inFlightFences[_currentFrame]) != Result.Success)
            {
                throw new Exception("failed to submit draw command buffer!");
            }

            var swapChains = stackalloc[]
			{
				_swapchain
			};

            PresentInfoKHR presentInfo = new()
            {
                SType = StructureType.PresentInfoKhr,

                WaitSemaphoreCount = 1,
                PWaitSemaphores = signalSemaphores,

                SwapchainCount = 1,
                PSwapchains = swapChains,

                PImageIndices = &imageIndex
            };

            _khrSwapChain.QueuePresent(_presentQueue, in presentInfo);

            _currentFrame = (_currentFrame + 1) % _config.MaxFramesInFlight;
        }
	}
}
