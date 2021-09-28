using System;

namespace UnityEngine.Rendering.HighDefinition
{
    [Serializable, VolumeComponentMenuForRenderPipeline("Lighting/WaterRendering", typeof(HDRenderPipeline))]
    [HDRPHelpURLAttribute("Override-Water-Rendering")]
    public sealed partial class WaterRendering : VolumeComponent
    {
        public enum WaterGridResolution
        {
            VeryLow128 = 128,
            Low256 = 256,
            Medium512 = 512,
            High1024 = 1024,
            Ultra2048 = 2048,
        }

        [Serializable]
        public sealed class WaterGridResolutionParameter : VolumeParameter<WaterGridResolution>
        {
            public WaterGridResolutionParameter(WaterGridResolution value, bool overrideState = false) : base(value, overrideState) { }
        }

        public BoolParameter enable = new BoolParameter(false);
        public WaterGridResolutionParameter gridResolution = new WaterGridResolutionParameter(WaterGridResolution.Medium512);
        public MinFloatParameter gridSize = new MinFloatParameter(1000.0f, 100.0f);
        public MinIntParameter numLevelOfDetais = new MinIntParameter(4, 0);

        WaterRendering()
        {
            displayName = "WaterRendering";
        }
    }
}
