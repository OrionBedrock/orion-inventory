namespace OrionInventory.Handlers;

using Orion.Api;
using Orion.Api.Items;
using Orion.Api.Network;
using Orion.Containers;
using Orion.Protocol.Enums;
using Orion.Protocol.Io;
using Orion.Protocol.Packets;
using Orion.Protocol.Types;
using OrionInventory;
using ApiContainer = Orion.Api.Containers.IContainer;
using ProtoItemStackRequest = Orion.Protocol.Types.ItemStackRequest;

public static class ItemStackRequestHandler
{
    public static void Handle(IPlayer player, ReadOnlySpan<byte> packetBuffer)
    {
        ItemStackRequestPacket packet;
        try
        {
            packet = (ItemStackRequestPacket)PacketCodec.DeserializeFromBytes(packetBuffer);
        }
        catch (Exception exception)
        {
            CreativeInventoryLog.LogItemStackAction("?", "deserialize-fail", exception.ToString());
            throw;
        }

        if (packet.Requests.Count == 0)
        {
            return;
        }

        CreativeInventoryLog.LogItemStackAction(
            player.Username,
            "packet",
            $"requests={packet.Requests.Count} bytes={packetBuffer.Length}");

        List<ItemStackResponse> responses = new(packet.Requests.Count);
        foreach (ProtoItemStackRequest request in packet.Requests)
        {
            try
            {
                ItemStackResponse response = ProcessRequest(player, request);
                responses.Add(response);
                CreativeInventoryLog.LogItemStackAction(
                    player.Username,
                    "response",
                    $"req={request.RequestId} status={response.Status} containers={response.ContainerInfo.Count}");
            }
            catch (Exception exception)
            {
                CreativeInventoryLog.LogItemStackAction(
                    player.Username,
                    "exception",
                    $"req={request.RequestId} {exception}");
                responses.Add(ErrorResponse(request.RequestId));
            }
        }

        CreativeInventoryLog.LogItemStackResponse(player.Username, responses.Count, responses);

        ItemStackResponsePacket responsePacket = new() { Responses = responses };
        player.Send(new OpaqueOutboundPacket(responsePacket));
    }

    [ThreadStatic]
    private static int _pendingCreativeStackId;

    [ThreadStatic]
    private static IItemStack? _pendingCreativeItem;

    public static ItemStackResponse Process(IPlayer player, ProtoItemStackRequest request) =>
        ProcessRequest(player, request);

    private static ItemStackResponse ProcessRequest(IPlayer player, ProtoItemStackRequest request)
    {
        Dictionary<string, StackResponseContainerInfo> changed = [];
        _pendingCreativeStackId = 0;
        _pendingCreativeItem = null;

        foreach (IStackRequestAction action in request.Actions)
        {
            ItemStackResponseStatus status = HandleAction(player, action, changed);
            if (status == ItemStackResponseStatus.Ok)
            {
                CreativeInventoryLog.LogItemStackAction(
                    player.Username,
                    "action-ok",
                    DescribeAction(action));
                continue;
            }

            CreativeInventoryLog.LogItemStackAction(
                player.Username,
                "action-fail",
                $"status={status} {DescribeAction(action)}");
            Console.WriteLine(
                $"[ItemStackRequest] Failed: request: {request.RequestId} status: {status} action: {DescribeAction(action)}");
            ResyncContainers(player);
            return ErrorResponse(request.RequestId, status);
        }

        return new ItemStackResponse
        {
            Status = ItemStackResponseStatus.Ok,
            RequestId = request.RequestId,
            ContainerInfo = changed.Count > 0 ? [.. changed.Values] : []
        };
    }

    private static ItemStackResponseStatus HandleAction(
        IPlayer player,
        IStackRequestAction action,
        Dictionary<string, StackResponseContainerInfo> changed)
    {
        return action switch
        {
            TransferStackRequestAction transfer => HandleTransfer(player, transfer, changed),
            SwapStackRequestAction swap => HandleSwap(player, swap, changed),
            DropStackRequestAction drop => HandleDrop(player, drop, changed),
            DestroyStackRequestAction destroy => HandleDestroy(player, destroy, changed),
            CraftCreativeStackRequestAction creative => HandleCraftCreative(player, creative),
            EmptyStackRequestAction => ItemStackResponseStatus.Ok,
            CraftResultsDeprecatedStackRequestAction => ItemStackResponseStatus.Ok,
            _ => ItemStackResponseStatus.InvalidRequestActionType
        };
    }

    private static ItemStackResponseStatus HandleTransfer(
        IPlayer player,
        TransferStackRequestAction action,
        Dictionary<string, StackResponseContainerInfo> changed)
    {
        if (_pendingCreativeItem is not null
            && IsCreatedOutputContainer(action.Source.Container.ContainerId))
        {
            if (!TryResolveSlot(player, action.Destination, out Container creativeDestination, out int creativeSlot))
            {
                CreativeInventoryLog.LogItemStackAction(
                    player.Username,
                    "creative-transfer-dst-miss",
                    Slot(action.Destination));
                return ItemStackResponseStatus.InvalidSourceContainer;
            }

            IItemStack item = _pendingCreativeItem;
            _pendingCreativeItem = null;
            creativeDestination.SetItem(creativeSlot, item);
            RecordChange(
                changed,
                action.Destination.Container,
                creativeDestination,
                action.Destination.Slot,
                creativeSlot);
            return ItemStackResponseStatus.Ok;
        }

        if (!TryResolveSlot(player, action.Source, out Container sourceContainer, out int sourceSlot) ||
            !TryResolveSlot(player, action.Destination, out Container destinationContainer, out int destinationSlot))
        {
            return ItemStackResponseStatus.InvalidSourceContainer;
        }

        IItemStack? sourceItem = sourceContainer.GetItem(sourceSlot);
        if (sourceItem is null && action.Source.StackNetworkId != 0 &&
            TryFindSlotByStackNetworkId(sourceContainer, action.Source.StackNetworkId, out int correctedSlot))
        {
            sourceSlot = correctedSlot;
            sourceItem = sourceContainer.GetItem(sourceSlot);
        }

        if (sourceItem is null)
        {
            return ItemStackResponseStatus.FailedToMatchExpectedSlotConsumedItem;
        }

        int amount = Math.Clamp((int)action.Count, 1, sourceItem.Count);
        if (action.Destination.StackNetworkId == 0)
        {
            int resolved = ResolveDestinationSlot(destinationContainer, sourceItem, destinationSlot);
            if (resolved >= 0)
            {
                destinationSlot = resolved;
            }
        }

        IItemStack? destinationItem = destinationContainer.GetItem(destinationSlot);
        if (destinationItem is null)
        {
            IItemStack? taken = sourceContainer.TakeItem(sourceSlot, amount);
            if (taken is null || taken.Count == 0)
            {
                return ItemStackResponseStatus.CannotRemoveItem;
            }

            destinationContainer.SetItem(destinationSlot, taken);
        }
        else
        {
            if (!sourceItem.CanStackWith(destinationItem))
            {
                return ItemStackResponseStatus.CannotPlaceItem;
            }

            int available = destinationItem.Type.MaxStackSize - destinationItem.Count;
            if (available <= 0)
            {
                return ItemStackResponseStatus.CannotPlaceItem;
            }

            amount = Math.Min(amount, available);
            sourceItem.Decrement(amount);
            destinationItem.Increment(amount);
            if (sourceItem.Count == 0)
            {
                sourceContainer.ClearSlot(sourceSlot);
            }
            else
            {
                sourceContainer.UpdateSlot(sourceSlot);
            }

            destinationContainer.UpdateSlot(destinationSlot);
        }

        RecordChange(changed, action.Source.Container, sourceContainer, action.Source.Slot, sourceSlot);
        RecordChange(
            changed,
            action.Destination.Container,
            destinationContainer,
            action.Destination.Slot,
            destinationSlot);
        return ItemStackResponseStatus.Ok;
    }

    private static ItemStackResponseStatus HandleSwap(
        IPlayer player,
        SwapStackRequestAction action,
        Dictionary<string, StackResponseContainerInfo> changed)
    {
        if (!TryResolveSlot(player, action.Source, out Container sourceContainer, out int sourceSlot) ||
            !TryResolveSlot(player, action.Destination, out Container destinationContainer, out int destinationSlot))
        {
            return ItemStackResponseStatus.InvalidSourceContainer;
        }

        sourceContainer.SwapItems(sourceSlot, destinationSlot, destinationContainer);
        RecordChange(changed, action.Source.Container, sourceContainer, action.Source.Slot, sourceSlot);
        RecordChange(
            changed,
            action.Destination.Container,
            destinationContainer,
            action.Destination.Slot,
            destinationSlot);
        return ItemStackResponseStatus.Ok;
    }

    private static ItemStackResponseStatus HandleDrop(
        IPlayer player,
        DropStackRequestAction action,
        Dictionary<string, StackResponseContainerInfo> changed)
    {
        if (!TryResolveSlot(player, action.Source, out Container container, out int slot))
        {
            return ItemStackResponseStatus.InvalidSourceContainer;
        }

        IItemStack? removed = container.TakeItem(slot, Math.Max(1, (int)action.Count));
        if (removed is null)
        {
            return ItemStackResponseStatus.CannotDropItem;
        }

        _ = player.DropItem(removed);
        RecordChange(changed, action.Source.Container, container, action.Source.Slot, slot);
        return ItemStackResponseStatus.Ok;
    }

    private static ItemStackResponseStatus HandleDestroy(
        IPlayer player,
        DestroyStackRequestAction action,
        Dictionary<string, StackResponseContainerInfo> changed)
    {
        if (!TryResolveSlot(player, action.Source, out Container container, out int slot))
        {
            return ItemStackResponseStatus.InvalidSourceContainer;
        }

        IItemStack? removed = container.TakeItem(slot, Math.Max(1, (int)action.Count));
        if (removed is null)
        {
            return ItemStackResponseStatus.CannotDestroyItem;
        }

        RecordChange(changed, action.Source.Container, container, action.Source.Slot, slot);
        return ItemStackResponseStatus.Ok;
    }

    private static ItemStackResponseStatus HandleCraftCreative(
        IPlayer player,
        CraftCreativeStackRequestAction action)
    {
        if (player.Gamemode != Orion.Api.Gamemode.Creative)
        {
            return ItemStackResponseStatus.PlayerNotInCreativeMode;
        }

        IItemStack? item = Items.TryCreateCreative((uint)action.CreativeItemNetworkId);
        if (item is null)
        {
            CreativeInventoryLog.LogItemStackAction(
                player.Username,
                "craft-creative-miss",
                $"creativeId={action.CreativeItemNetworkId}");
            return ItemStackResponseStatus.FailedToCraftCreative;
        }

        CreativeInventoryLog.LogItemStackAction(
            player.Username,
            "craft-creative",
            $"creativeId={action.CreativeItemNetworkId} item={item.Type.Identifier} net={item.Type.NetworkId} stackId={item.NetworkStackId}");
        _pendingCreativeItem = item;
        _pendingCreativeStackId = item.NetworkStackId;
        return ItemStackResponseStatus.Ok;
    }

    private static bool IsCreatedOutputContainer(byte containerId) =>
        containerId is (byte)ContainerName.CreativeOutput or (byte)ContainerId.CreatedOutput;

    private static bool TryResolveSlot(
        IPlayer player,
        StackRequestSlotInfo requestSlot,
        out Container container,
        out int slot)
    {
        container = null!;
        slot = -1;

        Container? resolved = ResolveContainer(player, requestSlot.Container, requestSlot.Slot);
        if (resolved is null)
        {
            return false;
        }

        int resolvedSlot = ResolveSlotIndex(requestSlot.Container, resolved, requestSlot.Slot);
        if (resolvedSlot < 0 || resolvedSlot >= resolved.GetSize())
        {
            return false;
        }

        container = resolved;
        slot = resolvedSlot;
        return true;
    }

    private static Container? ResolveContainer(IPlayer player, FullContainerName name, int slot)
    {
        if (TryGetOpenedDynamicContainer(player, name, out Container openedContainer))
        {
            return slot < openedContainer.GetSize()
                ? openedContainer
                : player.GetTrait<EntityInventoryTrait>()?.Container;
        }

        if (name.ContainerId == (byte)ContainerId.DynamicContainer)
        {
            return null;
        }

        return InventoryResolver.Resolve(player, name);
    }

    private static int ResolveSlotIndex(FullContainerName containerName, Container container, int slot)
    {
        if (containerName.ContainerId is (byte)ContainerName.CreativeOutput or (byte)ContainerId.CreatedOutput)
        {
            return 0;
        }

        if (containerName.ContainerId is (byte)ContainerId.Armor or 12
            or (byte)ContainerId.Inventory or (byte)ContainerId.Hotbar
            or (byte)ContainerId.FixedInventory or (byte)ContainerId.Offhand)
        {
            return NormalizeInventorySlot(slot);
        }

        if (containerName.ContainerId is (byte)ContainerId.DynamicContainer
            or (byte)ContainerId.Barrel or (byte)ContainerId.InventoryUi)
        {
            if (container.Type != ContainerType.Inventory)
            {
                if (slot >= 0 && slot < container.GetSize())
                {
                    return slot;
                }

                if (container.GetSize() == 27 && slot is >= 27 and <= 53)
                {
                    return slot - 27;
                }
            }

            return NormalizeInventorySlot(slot);
        }

        return slot;
    }

    private static int NormalizeInventorySlot(int slot) =>
        slot is >= 36 and <= 44 ? slot - 36 : slot;

    private static int ResolveDestinationSlot(Container container, IItemStack sourceItem, int preferredSlot)
    {
        if (preferredSlot >= 0 && preferredSlot < container.GetSize())
        {
            IItemStack? preferred = container.GetItem(preferredSlot);
            if (preferred is null ||
                preferred.CanStackWith(sourceItem) && preferred.Count < preferred.Type.MaxStackSize)
            {
                return preferredSlot;
            }
        }

        for (int i = 0; i < container.GetSize(); i++)
        {
            IItemStack? item = container.GetItem(i);
            if (item is not null && item.CanStackWith(sourceItem) && item.Count < item.Type.MaxStackSize)
            {
                return i;
            }
        }

        for (int i = 0; i < container.GetSize(); i++)
        {
            if (container.GetItem(i) is null)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryGetOpenedDynamicContainer(
        IPlayer player,
        FullContainerName name,
        out Container container)
    {
        container = null!;
        if (name.ContainerId != (byte)ContainerId.DynamicContainer)
        {
            return false;
        }

        if (name.DynamicContainerId.HasValue)
        {
            if (!player.TryGetOpenContainer((int)name.DynamicContainerId.Value, out ApiContainer? opened) ||
                opened is not Container concrete || concrete.Type == ContainerType.Inventory)
            {
                return false;
            }

            container = concrete;
            return true;
        }

        Container? single = null;
        foreach ((_, ApiContainer opened) in player.OpenedContainers)
        {
            if (opened is not Container concrete || concrete.Type == ContainerType.Inventory)
            {
                continue;
            }

            if (single is not null)
            {
                return false;
            }

            single = concrete;
        }

        if (single is null)
        {
            return false;
        }

        container = single;
        return true;
    }

    private static bool TryFindSlotByStackNetworkId(Container container, int stackNetworkId, out int slot)
    {
        slot = -1;
        if (stackNetworkId == 0)
        {
            return false;
        }

        int targetId = stackNetworkId < 0 && _pendingCreativeStackId != 0
            ? _pendingCreativeStackId
            : stackNetworkId;

        for (int i = 0; i < container.GetSize(); i++)
        {
            if (container.GetItem(i)?.NetworkStackId == targetId)
            {
                slot = i;
                return true;
            }
        }

        return false;
    }

    private static void RecordChange(
        Dictionary<string, StackResponseContainerInfo> changed,
        FullContainerName containerName,
        Container container,
        int responseSlot,
        int storageSlot)
    {
        string key = containerName.DynamicContainerId.HasValue
            ? $"{containerName.ContainerId}:{containerName.DynamicContainerId.Value}"
            : containerName.ContainerId.ToString();

        if (!changed.TryGetValue(key, out StackResponseContainerInfo? info))
        {
            info = new StackResponseContainerInfo
            {
                Container = new FullContainerName
                {
                    ContainerId = containerName.ContainerId,
                    DynamicContainerId = containerName.DynamicContainerId
                },
                SlotInfo = []
            };
            changed[key] = info;
        }

        IItemStack? item = container.GetItem(storageSlot);
        info.SlotInfo.RemoveAll(existing => existing.Slot == responseSlot);
        info.SlotInfo.Add(new StackResponseSlotInfo
        {
            Slot = (byte)responseSlot,
            HotbarSlot = (byte)responseSlot,
            Count = (byte)(item?.Count ?? 0),
            StackNetworkId = item?.NetworkStackId ?? 0,
            CustomName = string.Empty,
            FilteredCustomName = string.Empty,
            DurabilityCorrection = 0
        });
    }

    private static ItemStackResponse ErrorResponse(
        int requestId,
        ItemStackResponseStatus status = ItemStackResponseStatus.Error)
    {
        return new ItemStackResponse
        {
            Status = status,
            RequestId = requestId,
            ContainerInfo = []
        };
    }

    private static void ResyncContainers(IPlayer player)
    {
        foreach (ApiContainer container in player.OpenedContainers.Values.Distinct())
        {
            container.Update();
        }

        player.GetTrait<PlayerCursorTrait>()?.Container.UpdateSlot(0);
    }

    private static string DescribeAction(IStackRequestAction action)
    {
        return action switch
        {
            TransferStackRequestAction transfer =>
                $"Transfer(count: {transfer.Count}, src: {Slot(transfer.Source)}, dst: {Slot(transfer.Destination)})",
            SwapStackRequestAction swap => $"Swap(src: {Slot(swap.Source)}, dst: {Slot(swap.Destination)})",
            DropStackRequestAction drop => $"Drop(count: {drop.Count}, src: {Slot(drop.Source)})",
            DestroyStackRequestAction destroy => $"Destroy(count: {destroy.Count}, src: {Slot(destroy.Source)})",
            CraftCreativeStackRequestAction creative =>
                $"CraftCreative(id: {creative.CreativeItemNetworkId}, crafts: {creative.NumberOfCrafts})",
            _ => action.GetType().Name
        };
    }

    private static string Slot(StackRequestSlotInfo slot) =>
        $"[cid: {slot.Container.ContainerId}, dyn: {slot.Container.DynamicContainerId?.ToString() ?? "_"}, " +
        $"slot: {slot.Slot}, nid: {slot.StackNetworkId}]";
}

/// <summary>
/// Lightweight, plugin-local diagnostics shim (host <c>CreativeInventoryLog</c> lives in Orion.dll).
/// Kept as no-ops to preserve call sites without a host reference.
/// </summary>
internal static class CreativeInventoryLog
{
    public static void LogItemStackAction(string user, string kind, string detail)
    {
        _ = user;
        _ = kind;
        _ = detail;
    }

    public static void LogItemStackResponse(string user, int count, List<ItemStackResponse> responses)
    {
        _ = user;
        _ = count;
        _ = responses;
    }
}
