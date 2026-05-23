using System;
using System.Collections.Generic;
namespace Framework.Platform
{
    public class AppConfig
    {
        public string Title { get; init; } = "App";
        public int Width { get; init; } = 800;
        public int Height { get; init; } = 480;
        public bool Vsync { get; init; } = true;
        public bool Resizeable { get; init; } = false;
    }
}
