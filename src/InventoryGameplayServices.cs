using Orion.Api;
using Orion.Api.Items;
using Orion.Containers;
using Orion.Gameplay;
using Orion.Item;
using Orion.Player;
using Orion.Protocol.Enums;
using Orion.Protocol.Types;
using OrionInventory.Handlers;
using ApiContainer = Orion.Api.Containers.IContainer;
using CoreContainer = Orion.Containers.IContainer;

namespace OrionInventory;

sealed class InventoryAccess(EntityInventoryTrait trait) : IPlayerInventoryAccess
{
    public ApiContainer Container => trait.Container;
    public int SelectedSlot => trait.SelectedSlot;
    public void SetHeldSlot(int slot) => trait.SetHeldItem(slot);
    public IItemStack? GetHeldItem() => trait.GetHeldItem();
    public void Clear() => trait.Clear();
    public void SyncToPlayer(IPlayer player) => trait.SyncToPlayer(RequirePlayer(player));
    public void SyncHeldItemToClient(IPlayer player) => trait.SyncHeldItemToClient(RequirePlayer(player));

    static Player RequirePlayer(IPlayer player) =>
        player as Player ?? throw new ArgumentException("Player must be an Orion.Player.Player.", nameof(player));
}

public sealed class InventoryGameplayServices : IInventoryApi, IPlayerInventoryService
{
    public IPlayerInventoryService Inventory => this;

    public bool TryOpenInventory(IPlayer player)
    {
        Player concrete = RequirePlayer(player);
        EntityInventoryTrait? inventory = concrete.GetTrait<EntityInventoryTrait>();
        if (inventory is null)
        {
            return false;
        }

        inventory.Container.Show(concrete);
        return true;
    }

    public bool TryCloseInventory(IPlayer player, int windowId)
    {
        Player concrete = RequirePlayer(player);
        EntityInventoryTrait? inventory = concrete.GetTrait<EntityInventoryTrait>();
        if (inventory is not null && windowId == (inventory.Container.Identifier ?? 0))
        {
            inventory.Container.RemoveViewer(concrete, false);
            return true;
        }

        if (concrete.TryGetOpenContainer(windowId, out CoreContainer? open) && open is not null)
        {
            open.RemoveViewer(concrete, false);
            return true;
        }

        return false;
    }

    public bool TryGetAccess(IPlayer player, out IPlayerInventoryAccess? access)
    {
        Player concrete = RequirePlayer(player);
        EntityInventoryTrait? inventory = concrete.GetTrait<EntityInventoryTrait>();
        if (inventory is null)
        {
            access = null;
            return false;
        }

        access = new InventoryAccess(inventory);
        return true;
    }

    public IItemStack? GetHeldItem(IPlayer player) =>
        RequirePlayer(player).GetTrait<EntityInventoryTrait>()?.GetHeldItem();

    public bool TrySetHeldSlot(IPlayer player, int slot)
    {
        EntityInventoryTrait? inventory = RequirePlayer(player).GetTrait<EntityInventoryTrait>();
        if (inventory is null)
        {
            return false;
        }

        inventory.SetHeldItem(slot);
        return true;
    }

    public bool TryGive(IPlayer player, IItemStack stack, out int leftover)
    {
        ItemStack concreteStack = RequireStack(stack);
        leftover = concreteStack.StackSize;
        if (!TryGetAccess(player, out IPlayerInventoryAccess? access) || access is null)
        {
            return false;
        }

        ItemStack remaining = concreteStack.Clone();
        if (!access.Container.AddItem(remaining))
        {
            leftover = remaining.StackSize;
            return leftover < concreteStack.StackSize;
        }

        leftover = 0;
        access.SyncToPlayer(player);
        return true;
    }

    public bool TryClear(IPlayer player)
    {
        if (!TryGetAccess(player, out IPlayerInventoryAccess? access) || access is null)
        {
            return false;
        }

        access.Clear();
        return true;
    }

    public bool TryCollect(IPlayer player, IItemStack item, out ushort moved)
    {
        ItemStack concreteItem = RequireStack(item);
        moved = 0;
        Player concrete = RequirePlayer(player);
        EntityInventoryTrait? inventory = concrete.GetTrait<EntityInventoryTrait>();
        if (inventory is null || concreteItem.StackSize == 0)
        {
            return false;
        }

        Container container = inventory.Container;
        ushort remaining = concreteItem.StackSize;

        for (int i = 0; i < container.GetSize() && remaining > 0; i++)
        {
            ItemStack? existing = container.GetItem(i);
            if (existing is null || !existing.CanStackWith(concreteItem) || existing.StackSize >= existing.Type.MaxStackSize)
            {
                continue;
            }

            int space = existing.Type.MaxStackSize - existing.StackSize;
            int transfer = Math.Min(space, remaining);
            if (transfer <= 0)
            {
                continue;
            }

            existing.IncrementStack((ushort)transfer);
            container.UpdateSlot(i);
            remaining = (ushort)(remaining - transfer);
            moved = (ushort)(moved + transfer);
        }

        for (int i = 0; i < container.GetSize() && remaining > 0; i++)
        {
            if (container.GetItem(i) is not null)
            {
                continue;
            }

            ushort transfer = (ushort)Math.Min(remaining, concreteItem.Type.MaxStackSize);
            ItemStack stack = concreteItem.Clone(transfer);
            container.SetItem(i, stack);
            remaining = (ushort)(remaining - transfer);
            moved = (ushort)(moved + transfer);
        }

        if (moved == 0)
        {
            return false;
        }

        concreteItem.SetStackSize(remaining);
        inventory.SyncToPlayer(concrete);
        return true;
    }

    public bool TrySyncToClient(IPlayer player)
    {
        Player concrete = RequirePlayer(player);
        if (!concrete.Spawned || !TryGetAccess(player, out IPlayerInventoryAccess? access) || access is null)
        {
            return false;
        }

        EnsureContainerViewer(concrete, AsCore(access.Container), access.Container.Identifier ?? 0);
        access.SyncToPlayer(player);
        access.SyncHeldItemToClient(player);
        return true;
    }

    public void EnableHud(IPlayer player)
    {
        if (RequirePlayer(player).GetTrait<EntityInventoryTrait>() is null)
        {
            return;
        }

        RequirePlayer(player).SetHud(Orion.Protocol.Enums.HudVisibility.Reset, Orion.Protocol.Enums.HudElement.HotBar);
    }

    public ApiContainer? ResolveContainer(IPlayer player, ContainerNameWire name)
    {
        if (name.Value is not FullContainerName fullName)
        {
            return null;
        }

        Player concrete = RequirePlayer(player);
        EntityInventoryTrait? inventory = concrete.GetTrait<EntityInventoryTrait>();
        if (inventory is null)
        {
            return null;
        }

        if (fullName.ContainerId is (byte)ContainerId.Armor or 12
            or (byte)ContainerId.Inventory or (byte)ContainerId.Hotbar
            or (byte)ContainerId.FixedInventory or (byte)ContainerId.Offhand)
        {
            return inventory.Container;
        }

        if (fullName.ContainerId is (byte)ContainerId.Cursor or (byte)ContainerId.CreatedOutput
            or (byte)ContainerName.Cursor or (byte)ContainerName.CreativeOutput)
        {
            return concrete.GetTrait<PlayerCursorTrait>()?.Container;
        }

        if (fullName.ContainerId == (byte)ContainerId.Barrel || fullName.ContainerId == (byte)ContainerId.InventoryUi
            || fullName.ContainerId == (byte)ContainerName.Barrel || fullName.ContainerId == (byte)ContainerName.Container)
        {
            if (fullName.DynamicContainerId.HasValue &&
                concrete.TryGetOpenContainer((int)fullName.DynamicContainerId.Value!, out CoreContainer? containerById)
                && containerById is ApiContainer apiById)
            {
                return apiById;
            }

            foreach ((int _, CoreContainer candidate) in concrete.openedContainers)
            {
                if (candidate.Type != ContainerType.Inventory && candidate is ApiContainer apiCandidate)
                {
                    return apiCandidate;
                }
            }

            return inventory.Container;
        }

        ContainerName containerName = (ContainerName)fullName.ContainerId;
        switch (containerName)
        {
            case ContainerName.HotbarAndInventory:
            case ContainerName.Hotbar:
            case ContainerName.Inventory:
            case ContainerName.Armor:
            case ContainerName.Offhand:
                return inventory.Container;

            case ContainerName.Cursor:
            case ContainerName.CreativeOutput:
                return concrete.GetTrait<PlayerCursorTrait>()?.Container;
        }

        if (fullName.DynamicContainerId.HasValue &&
            concrete.TryGetOpenContainer((int)fullName.DynamicContainerId.Value!, out CoreContainer? container)
            && container is ApiContainer api)
        {
            return api;
        }

        return null;
    }

    public bool TryProcessItemStackRequest(
        IPlayer player,
        ItemStackRequestWire request,
        out ItemStackResponseWire response)
    {
        if (request.Value is not ItemStackRequest protocolRequest)
        {
            response = new ItemStackResponseWire(new ItemStackResponse
            {
                RequestId = 0,
                Status = ItemStackResponseStatus.Error
            });
            return false;
        }

        ItemStackResponse protocolResponse = ItemStackRequestHandler.Process(RequirePlayer(player), protocolRequest);
        response = new ItemStackResponseWire(protocolResponse);
        return true;
    }

    static void EnsureContainerViewer(Player player, CoreContainer container, int windowId)
    {
        if (container is not Container concrete)
        {
            player.RegisterOpenContainer(windowId, container);
            return;
        }

        if (concrete.occupants.ContainsKey(player))
        {
            return;
        }

        concrete.occupants[player] = windowId;
        player.RegisterOpenContainer(windowId, concrete);
    }

    static Player RequirePlayer(IPlayer player) =>
        player as Player ?? throw new ArgumentException("Player must be an Orion.Player.Player.", nameof(player));

    static ItemStack RequireStack(IItemStack stack) =>
        stack as ItemStack ?? throw new ArgumentException("Stack must be an Orion.Item.ItemStack.", nameof(stack));

    static CoreContainer AsCore(ApiContainer container) =>
        container as CoreContainer
        ?? throw new ArgumentException("Container must implement Orion.Containers.IContainer.", nameof(container));
}
