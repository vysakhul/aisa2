using Aisa.Character;
using SkiaSharp;

namespace Aisa.CharacterVariants;

/// <summary>A complete visual interpretation of <see cref="CharacterPose"/>. Lengths in the pose are face-diameter units.</summary>
interface ICharacterDesign
{
    string Name { get; }
    string Tagline { get; }
    void DrawHalo(SKCanvas c, CharacterPose p, SKPoint center, float diameter);
    void Draw(SKCanvas c, CharacterPose p, SKPoint center, float diameter);
}
