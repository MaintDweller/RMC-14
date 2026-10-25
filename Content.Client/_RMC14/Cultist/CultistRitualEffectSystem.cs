using System.Numerics;
using Content.Shared._RMC14.Cultist;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client._RMC14.Cultist;

public sealed class CultistRitualEffectSystem : EntitySystem
{
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void FrameUpdate(float frameTime)
    {
        var query = EntityQueryEnumerator<CultistRitualEffectComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var effect, out var sprite))
        {
            var elapsed = Math.Max(0, (float) (_timing.CurTime - effect.StartedAt).TotalSeconds);
            var fadeIn = effect.FadeIn > 0 ? Math.Clamp(elapsed / effect.FadeIn, 0, 1) : 1;
            var phase = effect.PulsePeriod > 0 ? elapsed / effect.PulsePeriod + effect.PhaseOffset : 0;
            var pulse = (1 - MathF.Cos(phase * MathF.Tau)) / 2;
            var duration = (float) (effect.EndsAt - effect.StartedAt).TotalSeconds;
            var buildup = duration > 0 ? Math.Clamp(elapsed / duration, 0, 1) : 1;
            var alpha = fadeIn * MathHelper.Lerp(effect.MinimumAlpha, effect.MaximumAlpha, pulse);
            alpha *= MathHelper.Lerp(1 - effect.ProgressOpacity, 1, buildup);
            var scale = 1 + effect.Expansion * pulse + effect.ProgressExpansion * buildup;
            var offset = effect.Offset + effect.Drift * pulse;
            offset.X += effect.Sway * MathF.Sin(phase * MathF.Tau);
            var rotation = new Angle(effect.Rocking * MathF.Sin(phase * MathF.Tau));
            var travel = Vector2.Zero;
            if (effect.TravelToCenter)
            {
                if (!TryComp<TransformComponent>(effect.Focus, out var focus))
                {
                    _sprite.SetColor((uid, sprite), Color.Transparent);
                    continue;
                }

                // Sprite offsets face the camera; map displacement must use the same coordinate space.
                var delta = _transform.GetWorldPosition(focus) - _transform.GetWorldPosition(uid);
                travel = (_eye.CurrentEye?.Rotation ?? Angle.Zero).RotateVec(delta);
                offset += travel * (phase % 1);
                alpha *= MathF.Pow(MathF.Sin((phase % 1) * MathF.PI), 2);
                rotation = new Angle(travel);
            }

            if (effect.CompletedAt is { } completedAt)
            {
                var completedElapsed = Math.Max(0, (float) (_timing.CurTime - completedAt).TotalSeconds);
                var progress = effect.CompletionDuration > 0
                    ? Math.Clamp(completedElapsed / effect.CompletionDuration, 0, 1)
                    : 1;
                alpha = effect.MaximumAlpha * (1 - progress);
                scale = (1 + effect.ProgressExpansion) * MathHelper.Lerp(1, effect.CompletionScale, progress);
                offset = effect.Offset + effect.CompletionDrift * progress;
                if (effect.TravelToCenter)
                {
                    var stoppedElapsed = (float) (completedAt - effect.StartedAt).TotalSeconds;
                    var stoppedPhase = effect.PulsePeriod > 0
                        ? stoppedElapsed / effect.PulsePeriod + effect.PhaseOffset
                        : 0;
                    offset += travel * MathHelper.Lerp(stoppedPhase % 1, 1, progress);
                }
            }

            var color = effect.Color;
            _sprite.SetColor((uid, sprite), color.WithAlpha(color.A * alpha));
            _sprite.SetScale((uid, sprite), effect.Scale * scale);
            _sprite.SetOffset((uid, sprite), offset);
            _sprite.SetRotation((uid, sprite), rotation);
        }
    }
}
