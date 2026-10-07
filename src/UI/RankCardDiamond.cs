using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoidCrewProgressEditor
{
    internal sealed class RankCardDiamond : Image
    {
        private static Texture2D artwork;
        internal float AspectRatio { get { return (float)artwork.height / artwork.width; } }
        internal RankCardDiamond()
        {
            if (artwork == null)
            {
                using (var stream = typeof(RankCardDiamond).Assembly.GetManifestResourceStream("ProgressEditor.RankCardBackground.png"))
                {
                    if (stream == null) throw new InvalidOperationException("Rank card artwork is missing.");
                    using (var buffer = new MemoryStream())
                    {
                        stream.CopyTo(buffer);
                        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (!ImageConversion.LoadImage(texture, buffer.ToArray()))
                        {
                            UnityEngine.Object.Destroy(texture);
                            throw new InvalidOperationException("Rank card artwork could not be decoded.");
                        }
                        texture.name = "ProgressEditorRankCardBackground";
                        texture.hideFlags = HideFlags.HideAndDontSave;
                        artwork = texture;

                    }
                }
            }
            image = artwork;
            scaleMode = ScaleMode.StretchToFill;
            pickingMode = PickingMode.Ignore;
        }
    }
}
