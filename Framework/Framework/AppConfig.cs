namespace Framework
{
    public class AppConfig
    {
        public string Title { get; init; } = "App";
        public int Width { get; init; } = 800;
        public int Height { get; init; } = 480;
        public bool Vsync { get; init; } = true;
        public bool Resizeable { get; init; } = false;

        public bool EnableValidationLayers = true;
        public readonly string[] ValidationLayers = new string[]
        {
            "VK_LAYER_KHRONOS_validation"
        };
    }
}
