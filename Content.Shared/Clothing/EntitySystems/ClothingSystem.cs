using Content.Shared.Clothing.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Strip.Components;
using Robust.Shared.GameStates;

namespace Content.Shared.Clothing.EntitySystems;

public abstract class ClothingSystem : EntitySystem
{
    [Dependency] private readonly SharedItemSystem _itemSys = default!;
    [Dependency] private readonly InventorySystem _invSystem = default!;
    [Dependency] private readonly SharedHandsSystem _handsSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ClothingComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<ClothingComponent, ComponentGetState>(OnGetState);
        SubscribeLocalEvent<ClothingComponent, ComponentHandleState>(OnHandleState);
        SubscribeLocalEvent<ClothingComponent, GotEquippedEvent>(OnGotEquipped);
        SubscribeLocalEvent<ClothingComponent, GotUnequippedEvent>(OnGotUnequipped);
        SubscribeLocalEvent<ClothingComponent, ClothingEquipDoAfterEvent>(OnEquipDoAfter);
        SubscribeLocalEvent<ClothingComponent, ClothingUnequipDoAfterEvent>(OnUnequipDoAfter);
        SubscribeLocalEvent<ClothingComponent, BeforeItemStrippedEvent>(OnItemStripped);
    }

    private void OnUseInHand(Entity<ClothingComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || !ent.Comp.QuickEquip)
            return;

        var user = args.User;
        if (!TryComp(user, out InventoryComponent? inv) ||
            !TryComp(user, out HandsComponent? hands))
            return;

        QuickEquip(ent, (user, inv, hands));
        args.Handled = true;
        args.ApplyDelay = false;
    }

    private void QuickEquip(Entity<ClothingComponent> toEquipEnt, Entity<InventoryComponent, HandsComponent> userEnt)
    {
        foreach (var slotDef in userEnt.Comp1.Slots)
        {
            if (!_invSystem.CanEquip(userEnt, toEquipEnt, slotDef.Name, out _, slotDef, userEnt, toEquipEnt))
                continue;

            if (_invSystem.TryGetSlotEntity(userEnt, slotDef.Name, out var slotEntity, userEnt))
            {
                // Item in slot has to be quick equipable as well
                if (TryComp(slotEntity, out ClothingComponent? item) && !item.QuickEquip)
                    continue;

                if (!_invSystem.TryUnequip(userEnt, slotDef.Name, true, inventory: userEnt, checkDoafter: true))
                    continue;

                if (!_invSystem.TryEquip(userEnt, toEquipEnt, slotDef.Name, inventory: userEnt, clothing: toEquipEnt, checkDoafter: true, triggerHandContact: true))
                    continue;

                _handsSystem.PickupOrDrop(userEnt, slotEntity.Value, handsComp: userEnt);
            }
            else
            {
                if (!_invSystem.TryEquip(userEnt, toEquipEnt, slotDef.Name, inventory: userEnt, clothing: toEquipEnt, checkDoafter: true, triggerHandContact: true))
                    continue;
            }

            break;
        }
    }

    protected virtual void OnGotEquipped(EntityUid uid, ClothingComponent component, GotEquippedEvent args)
    {
        component.InSlot = args.Slot;
        component.InSlotFlag = args.SlotFlags;

        if ((component.Slots & args.SlotFlags) == SlotFlags.NONE)
            return;

        var gotEquippedEvent = new ClothingGotEquippedEvent(args.Equipee, component);
        RaiseLocalEvent(uid, ref gotEquippedEvent);

        var didEquippedEvent = new ClothingDidEquippedEvent((uid, component));
        RaiseLocalEvent(args.Equipee, ref didEquippedEvent);
    }

    protected virtual void OnGotUnequipped(EntityUid uid, ClothingComponent component, GotUnequippedEvent args)
    {
        if ((component.Slots & args.SlotFlags) != SlotFlags.NONE)
        {
            var gotUnequippedEvent = new ClothingGotUnequippedEvent(args.Equipee, component);
            RaiseLocalEvent(uid, ref gotUnequippedEvent);

            var didUnequippedEvent = new ClothingDidUnequippedEvent((uid, component));
            RaiseLocalEvent(args.Equipee, ref didUnequippedEvent);
        }

        component.InSlot = null;
        component.InSlotFlag = null;
    }

    private void OnGetState(EntityUid uid, ClothingComponent component, ref ComponentGetState args)
    {
        args.State = new ClothingComponentState(component.EquippedPrefix);
    }

    private void OnHandleState(EntityUid uid, ClothingComponent component, ref ComponentHandleState args)
    {
        if (args.Current is not ClothingComponentState state)
            return;

        SetEquippedPrefix(uid, state.EquippedPrefix, component);
    }

    private void OnEquipDoAfter(Entity<ClothingComponent> ent, ref ClothingEquipDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } target)
            return;
        args.Handled = _invSystem.TryEquip(args.User, target, ent, args.Slot, clothing: ent.Comp, predicted: true, checkDoafter: false);
    }

    private void OnUnequipDoAfter(Entity<ClothingComponent> ent, ref ClothingUnequipDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } target)
            return;
        args.Handled = _invSystem.TryUnequip(args.User, target, args.Slot, clothing: ent.Comp, predicted: true, checkDoafter: false, triggerHandContact: true);
        if (args.Handled)
            _handsSystem.TryPickup(args.User, ent);
    }

    private void OnItemStripped(Entity<ClothingComponent> ent, ref BeforeItemStrippedEvent args)
    {
        args.Additive += ent.Comp.StripDelay;
    }

    #region Public API

    public void SetEquippedPrefix(EntityUid uid, string? prefix, ClothingComponent? clothing = null)
    {
        if (!Resolve(uid, ref clothing, false))
            return;

        if (clothing.EquippedPrefix == prefix)
            return;

        clothing.EquippedPrefix = prefix;
        _itemSys.VisualsChanged(uid);
        Dirty(uid, clothing);
    }

    public void SetSlots(EntityUid uid, SlotFlags slots, ClothingComponent? clothing = null)
    {
        if (!Resolve(uid, ref clothing))
            return;

        clothing.Slots = slots;
        Dirty(uid, clothing);
    }

    /// <summary>
    ///     Copy all clothing specific visuals from another item.
    /// </summary>
    public void CopyVisuals(EntityUid uid, ClothingComponent otherClothing, ClothingComponent? clothing = null)
    {
        if (!Resolve(uid, ref clothing))
            return;

        clothing.ClothingVisuals = otherClothing.ClothingVisuals;
        clothing.EquippedPrefix = otherClothing.EquippedPrefix;
        clothing.RsiPath = otherClothing.RsiPath;

        _itemSys.VisualsChanged(uid);
        Dirty(uid, clothing);
    }

    public void SetLayerColor(ClothingComponent clothing, string slot, string mapKey, Color? color)
    {
        foreach (var layer in clothing.ClothingVisuals[slot])
        {
            if (layer.MapKeys == null)
                return;

            if (!layer.MapKeys.Contains(mapKey))
                continue;

            layer.Color = color;
        }
    }
    public void SetLayerState(ClothingComponent clothing, string slot, string mapKey, string state)
    {
        foreach (var layer in clothing.ClothingVisuals[slot])
        {
            if (layer.MapKeys == null)
                return;

            if (!layer.MapKeys.Contains(mapKey))
                continue;

            layer.State = state;
        }
    }

    #endregion
}
