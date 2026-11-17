using Aisa.Character.Rendering;
using SkiaSharp;

namespace Aisa.Character.Tests;

public class CharacterRendererTests
{
    [Theory]
    [InlineData(20)]
    [InlineData(44)]
    [InlineData(88)]
    public void Draws_every_eye_badge_and_particle_without_throwing(float diameter)
    {
        using var bmp = new SKBitmap(240, 240);
        using var canvas = new SKCanvas(bmp);
        var particles = Enum.GetValues<ParticleKind>().Select((k, i) => new Particle(k, -0.6f + i * 0.3f, -0.8f, 0.14f, 0.3f, 1f)).ToArray();

        foreach (var eyes in Enum.GetValues<EyeShape>())
        foreach (var badge in Enum.GetValues<BadgeKind>())
        {
            var pose = new CharacterPose { Eyes = eyes, Badge = badge, BadgeScale = 1, GlowOpacity = 0.65f, Blush = 1, Particles = particles };
            canvas.Clear(SKColors.Black);
            CharacterRenderer.DrawHalo(canvas, pose, new SKPoint(120, 120), diameter);
            CharacterRenderer.Draw(canvas, pose, new SKPoint(120, 120), diameter);
        }

        // Body centre is light, far corner stays black.
        Assert.True(bmp.GetPixel(120, 120 + (int)(diameter * 0.3f)).Red > 150);
        Assert.Equal(SKColors.Black, bmp.GetPixel(0, 0));
    }

    [Fact]
    public void Halo_fades_to_nothing_at_its_edge()
    {
        using var bmp = new SKBitmap(300, 300);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        var pose = new CharacterPose { Glow = new Rgb(1, 1, 1), GlowOpacity = 1 };
        CharacterRenderer.DrawHalo(canvas, pose, new SKPoint(150, 150), 100);

        Assert.True(bmp.GetPixel(150, 150).Red > 40);
        Assert.True(bmp.GetPixel(150 + 105, 150).Red < 8); // near r = 1.1 d
        Assert.Equal(SKColors.Black, bmp.GetPixel(150 + 115, 150));
    }
}
