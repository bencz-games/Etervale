using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Effects;
using PristonTale.Simulation.Items;
using PristonTale.Contracts.Motion;

namespace PristonTale.Gameplay.World.Remote;

// Draws an entity's skill look on its actors: the body, head and held items take the tint, opacity, wobble and overlay;
// a shield model swap, an enlarged weapon copy and afterimages are extra actors this presenter adds, keeps attached to
// the current body and removes when the look ends or the body is replaced. A held item's glow (PaintGlow) is drawn
// each frame on top of the look the item last took; an item that stops glowing returns to that look.
public sealed class ActorLookPresenter
{
    // The vague body's overlay: an item mix texture scrolled by the fifth scroll form (5/256 of a turn per 64 ms).
    public const string VagueOverlay = "image/sinImage/Items/DropItem/mixM_04.bmp";
    private const uint VagueOverlayForm = 9;
    private const string ShieldBone = "Bip01 L Forearm";

    private readonly IActorScene _scene;
    private readonly PristonActorLoader _loader;
    private readonly string _assetRoot;
    private readonly string _overlay;
    private readonly Dictionary<string, Task<PristonActor>> _shields = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ulong, Extras> _extras = new();
    // The look each held item last took (absent: normal), and the items that glowed this and the previous frame.
    private readonly Dictionary<ActorHandle, PristonActorAppearance> _held = new();
    private readonly HashSet<ActorHandle> _glowing = new();
    private readonly HashSet<ActorHandle> _glowed = new();
    private readonly HashSet<string> _overlays = new(StringComparer.OrdinalIgnoreCase);
    // The full path of each mix glow texture, made once instead of every frame per holder.
    private readonly Dictionary<string, string> _mixOverlays = new(StringComparer.OrdinalIgnoreCase);

    public ActorLookPresenter(IActorScene scene, PristonActorLoader loader, string assetRoot)
    {
        _scene = scene;
        _loader = loader;
        _assetRoot = assetRoot;
        _overlay = Path.Combine(assetRoot, VagueOverlay);
        _ = scene.PrepareActorOverlayAsync(_overlay);
    }

    // handles: the entity's actors now (none when it is gone); holdsShield: its off hand holds a shield.
    public void Apply(ulong entityId, PlayerActorHandles handles, bool holdsShield, in EntityLook look)
    {
        if (!_extras.TryGetValue(entityId, out var extras))
        {
            if (look.IsNormal)
            {
                Paint(handles, PristonActorAppearance.Normal, PristonActorAppearance.Normal);
                return;
            }

            extras = new Extras();
            _extras.Add(entityId, extras);
        }

        if (extras.Body != handles.Body)
        {
            Remove(extras);
            extras.Body = handles.Body;
        }

        extras.OffHand = handles.OffHand;

        if (look.IsNormal || !handles.Body.IsValid)
        {
            Remove(extras);
            _extras.Remove(entityId);
            Paint(handles, PristonActorAppearance.Normal, PristonActorAppearance.Normal);
            return;
        }

        var body = new PristonActorAppearance(look.Tint, look.Opacity, look.Wobble, look.CastsShadow, look.Overlay ? _overlay : "",
            look.Overlay ? VagueOverlayForm : 0u, 1f);
        var swapped = UpdateShield(extras, handles, holdsShield ? look.ShieldModel : "");
        MoveShield(extras, handles, look.ShieldBone);
        var offHand = swapped ? body with { Opacity = 0f } : body with { Tint = body.Tint + look.ShieldGlow };
        Paint(handles, body, offHand);
        if (extras.Shield.IsValid)
        {
            _scene.SetActorAppearance(extras.Shield, body);
        }

        UpdateWeapon(extras, handles, look);
        UpdateAfterimages(extras, handles, look);
    }

    // One held item's glow this frame, at the clock in milliseconds.
    public void PaintGlow(ActorHandle handle, in HeldItemGlow glow, long nowMs)
    {
        if (!handle.IsValid || !glow.Glows)
        {
            return;
        }

        var look = _held.TryGetValue(handle, out var held) ? held : PristonActorAppearance.Normal;
        var sample = glow.Sample(nowMs);
        var overlay = look.Overlay;
        var form = look.OverlayForm;
        if (glow.MixTexture.Length > 0)
        {
            if (!_mixOverlays.TryGetValue(glow.MixTexture, out overlay))
            {
                overlay = Path.Combine(_assetRoot, glow.MixTexture);
                _mixOverlays.Add(glow.MixTexture, overlay);
            }

            form = (uint)glow.MixScroll;
            if (_overlays.Add(overlay))
            {
                _ = _scene.PrepareActorOverlayAsync(overlay);
            }
        }

        _scene.SetActorAppearance(handle, look with
        {
            Tint = look.Tint + new Vector3(sample.R, sample.G, sample.B),
            Opacity = look.Opacity * sample.Opacity,
            Overlay = overlay,
            OverlayForm = form,
            Scale = look.Scale * sample.Scale
        });
        _glowing.Add(handle);
    }

    // Hides a held item this frame (a javelin in flight); it returns to its look once no longer hidden.
    public void PaintHidden(ActorHandle handle)
    {
        if (!handle.IsValid)
        {
            return;
        }

        var look = _held.TryGetValue(handle, out var held) ? held : PristonActorAppearance.Normal;
        _scene.SetActorAppearance(handle, look with { Opacity = 0f });
        _glowing.Add(handle);
    }

    // Returns the items that glowed last frame and not in this one to their look.
    public void EndGlowFrame()
    {
        foreach (var handle in _glowed)
        {
            if (!_glowing.Contains(handle))
            {
                _scene.SetActorAppearance(handle, _held.TryGetValue(handle, out var look) ? look : PristonActorAppearance.Normal);
            }
        }

        _glowed.Clear();
        _glowed.UnionWith(_glowing);
        _glowing.Clear();
    }

    // Removes every extra actor (scene change).
    public void Clear()
    {
        foreach (var extras in _extras.Values)
        {
            Remove(extras);
        }

        _extras.Clear();
        _held.Clear();
        _glowed.Clear();
        _glowing.Clear();
    }

    private void Paint(PlayerActorHandles handles, in PristonActorAppearance body, in PristonActorAppearance offHand)
    {
        foreach (var handle in (ReadOnlySpan<ActorHandle>)[handles.Body, handles.Head, handles.Weapon])
        {
            if (handle.IsValid)
            {
                _scene.SetActorAppearance(handle, body);
            }
        }

        if (handles.OffHand.IsValid)
        {
            _scene.SetActorAppearance(handles.OffHand, offHand);
        }

        Hold(handles.Weapon, body);
        Hold(handles.OffHand, offHand);
    }

    private void Hold(ActorHandle handle, in PristonActorAppearance look)
    {
        if (!handle.IsValid)
        {
            return;
        }

        if (look == PristonActorAppearance.Normal)
        {
            _held.Remove(handle);
        }
        else
        {
            _held[handle] = look;
        }
    }

    // True while the swap model is drawn instead of the held shield.
    private bool UpdateShield(Extras extras, PlayerActorHandles handles, string model)
    {
        if (model.Length == 0 || !handles.OffHand.IsValid)
        {
            RemoveShield(extras);
            return false;
        }

        if (!string.Equals(extras.ShieldModel, model, StringComparison.OrdinalIgnoreCase))
        {
            RemoveShield(extras);
            extras.ShieldModel = model;
        }

        if (extras.Shield.IsValid)
        {
            return true;
        }

        var loading = LoadShield(model);
        if (!loading.IsCompleted)
        {
            return false;
        }

        if (!loading.IsCompletedSuccessfully)
        {
            return false;
        }

        extras.Shield = _scene.AddActor(new PristonActorInstance(loading.Result, Matrix.Identity, MotionCode.Stand));
        _scene.AttachActor(extras.Shield, handles.Body, ShieldBone);
        return true;
    }

    // A thrown or swung shield follows another bone of the body during the cast, and returns to the forearm after.
    private void MoveShield(Extras extras, PlayerActorHandles handles, string bone)
    {
        var wanted = bone.Length > 0 ? bone : ShieldBone;
        if (!handles.OffHand.IsValid || string.Equals(extras.OffHandBone, wanted, StringComparison.OrdinalIgnoreCase)
            || bone.Length == 0 && extras.OffHandBone.Length == 0)
        {
            return;
        }

        try
        {
            _scene.AttachActor(handles.OffHand, handles.Body, wanted);
            extras.OffHandBone = wanted == ShieldBone ? "" : wanted;
        }
        catch (InvalidDataException)
        {
            // A body without that bone keeps the shield on the forearm.
            extras.OffHandBone = "";
        }
    }

    private Task<PristonActor> LoadShield(string model)
    {
        if (!_shields.TryGetValue(model, out var loading))
        {
            var path = Path.Combine(_assetRoot, model);
            loading = Task.Run(async () =>
            {
                var actor = _loader.LoadRigid(path);
                await _scene.PrepareActorResourcesAsync(actor);
                return actor;
            });
            _shields.Add(model, loading);
        }

        return loading;
    }

    private void UpdateWeapon(Extras extras, PlayerActorHandles handles, in EntityLook look)
    {
        if (look.WeaponScale <= 0f || !handles.Weapon.IsValid)
        {
            RemoveHandle(ref extras.WeaponCopy);
            return;
        }

        if (!extras.WeaponCopy.IsValid)
        {
            extras.WeaponCopy = _scene.AddActorEcho(handles.Weapon, 0f, ActorHandle.None);
        }

        // The copy replaces the red light; the other channels and the overlay stay the body's.
        var tint = new Vector3(look.WeaponTint.X, look.Tint.Y, look.Tint.Z);
        _scene.SetActorAppearance(extras.WeaponCopy, new PristonActorAppearance(tint, look.WeaponOpacity, false, false, "", 0u, look.WeaponScale));
    }

    private void UpdateAfterimages(Extras extras, PlayerActorHandles handles, in EntityLook look)
    {
        if (look.Afterimages <= 0 || extras.EchoLag != look.AfterimageLagFrames || extras.Echoes.Count != look.Afterimages)
        {
            foreach (var echo in extras.Echoes)
            {
                Remove(echo);
            }

            extras.Echoes.Clear();
            extras.EchoLag = look.AfterimageLagFrames;
        }

        for (var index = extras.Echoes.Count; index < look.Afterimages; index++)
        {
            var lag = look.AfterimageLagFrames * (index + 1);
            var echoBody = _scene.AddActorEcho(handles.Body, lag, ActorHandle.None);
            var echo = new PlayerActorHandles(echoBody, handles.Head.IsValid ? _scene.AddActorEcho(handles.Head, lag, ActorHandle.None) : ActorHandle.None)
            {
                Weapon = handles.Weapon.IsValid ? _scene.AddActorEcho(handles.Weapon, 0f, echoBody) : ActorHandle.None,
                OffHand = handles.OffHand.IsValid ? _scene.AddActorEcho(handles.OffHand, 0f, echoBody) : ActorHandle.None
            };
            extras.Echoes.Add(echo);
        }

        for (var index = 0; index < extras.Echoes.Count; index++)
        {
            var opacity = MathF.Max(0f, look.AfterimageOpacity + look.AfterimageOpacityStep * index);
            var appearance = new PristonActorAppearance(look.Tint, opacity, false, false, "", 0u, 1f);
            var echo = extras.Echoes[index];
            foreach (var handle in (ReadOnlySpan<ActorHandle>)[echo.Body, echo.Head, echo.Weapon, echo.OffHand])
            {
                if (handle.IsValid)
                {
                    _scene.SetActorAppearance(handle, appearance);
                }
            }
        }
    }

    private void Remove(Extras extras)
    {
        if (extras.OffHandBone.Length > 0 && extras.OffHand.IsValid)
        {
            try
            {
                _scene.AttachActor(extras.OffHand, extras.Body, ShieldBone);
            }
            catch (ArgumentException)
            {
                // The body is gone with its shield.
            }
        }

        extras.OffHandBone = "";
        RemoveShield(extras);
        RemoveHandle(ref extras.WeaponCopy);
        foreach (var echo in extras.Echoes)
        {
            Remove(echo);
        }

        extras.Echoes.Clear();
    }

    private void Remove(PlayerActorHandles echo) => echo.Remove(_scene);

    private void RemoveShield(Extras extras)
    {
        RemoveHandle(ref extras.Shield);
        extras.ShieldModel = "";
    }

    private void RemoveHandle(ref ActorHandle handle)
    {
        if (handle.IsValid)
        {
            _scene.RemoveActor(handle);
        }

        handle = ActorHandle.None;
    }

    private sealed class Extras
    {
        public ActorHandle Body = ActorHandle.None;
        public ActorHandle Shield = ActorHandle.None;
        public string ShieldModel = "";
        // The bone the held shield is attached to while a cast moves it ("" = the forearm), and that shield.
        public string OffHandBone = "";
        public ActorHandle OffHand = ActorHandle.None;
        public ActorHandle WeaponCopy = ActorHandle.None;
        public float EchoLag;
        public readonly List<PlayerActorHandles> Echoes = new();

        public int Count => (Shield.IsValid ? 1 : 0) + (WeaponCopy.IsValid ? 1 : 0) + Echoes.Count;
    }
}
