using Robust.Client.Graphics;

namespace Content.Client._RMC14.Cultist;

public sealed class CultistSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlays = default!;

    public override void Initialize()
    {
        _overlays.AddOverlay(new CultistHudOverlay());
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<CultistHudOverlay>();
    }
}
