namespace OrionInventory.Handlers;

using Orion.Api;
using Orion.Api.Network;
using Orion.Protocol.Io;
using Orion.Protocol.Packets;
using OrionInventory;
using ApiContainer = Orion.Api.Containers.IContainer;

public static class ContainerCloseHandler
{
    public static void Handle(IPlayer player, ReadOnlySpan<byte> packetBuffer)
    {
        ContainerClosePacket packet = (ContainerClosePacket)PacketCodec.DeserializeFromBytes(packetBuffer);

        EntityInventoryTrait? inventory = player.GetTrait<EntityInventoryTrait>();

        if (inventory is not null && packet.WindowId == (byte)(inventory.Container.Identifier ?? 0))
        {
            inventory.Container.RemoveViewer(player, false);
        }
        else if (player.TryGetOpenContainer(packet.WindowId, out ApiContainer? openContainer) && openContainer is not null)
        {
            openContainer.RemoveViewer(player, false);
        }

        ContainerClosePacket response = new()
        {
            WindowId = packet.WindowId,
            ContainerType = packet.ContainerType,
            ServerSide = false
        };

        player.Send(new OpaqueOutboundPacket(response));
    }
}
