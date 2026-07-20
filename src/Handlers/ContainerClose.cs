namespace OrionInventory.Handlers;

using Orion;
using Orion.Network;
using Orion.Protocol.Packets;
using Orion.RakNet;
using OrionInventory;
using Orion.Network.Handlers;
using Orion.Protocol.Io;
using BinaryReader = Basalt.Binary.BinaryReader;

public static class ContainerCloseHandler
{
    public static void Handle(Server server, NetworkConnection connection, ReadOnlySpan<byte> packetBuffer)
    {
        ContainerClosePacket packet = new();
        int offset = 0;
        BinaryReader reader = new(packetBuffer, ref offset);
        packet = (ContainerClosePacket)Packet.Deserialize(reader);

        if (SessionLookup.TryGetPlayer(server, connection, out global::Orion.Player.Player? player))
        {
            ArgumentNullException.ThrowIfNull(player);

            EntityInventoryTrait? inventory = player.GetTrait<EntityInventoryTrait>();

            if (inventory is not null && packet.WindowId == (byte)(inventory.Container.Identifier ?? 0))
            {
                inventory.Container.RemoveViewer(player, false);
            }
            else if (player.TryGetOpenContainer(packet.WindowId, out Orion.Containers.IContainer? openContainer) && openContainer is not null)
            {
                openContainer.RemoveViewer(player, false);
            }

            player.FlushClientWorldStateSyncIfPending(force: true);
        }

        ContainerClosePacket response = new()
        {
            WindowId = packet.WindowId,
            ContainerType = packet.ContainerType,
            ServerSide = false
        };
        if (SessionLookup.TryGetSession(server, connection, out global::Orion.Player.PlayerSession? session))
        {
            session.Send(response);
        }
        else
        {
            server.Network.SendPacket(connection, response);
        }
    }
}
