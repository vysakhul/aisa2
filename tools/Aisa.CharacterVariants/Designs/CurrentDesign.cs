using Aisa.Character;
using Aisa.Character.Rendering;
using SkiaSharp;

namespace Aisa.CharacterVariants.Designs;

/// <summary>Design #0: the shipping renderer, unchanged.</summary>
sealed class CurrentDesign : ICharacterDesign
{
    public string Name => "#0 Glossy Squircle (current)";
    public string Tagline => "White marshmallow squircle, pill eyes, curled antenna with mood tip.";
    public void DrawHalo(SKCanvas c, CharacterPose p, SKPoint center, float diameter) => CharacterRenderer.DrawHalo(c, p, center, diameter);
    public void Draw(SKCanvas c, CharacterPose p, SKPoint center, float diameter) => CharacterRenderer.Draw(c, p, center, diameter);
}
