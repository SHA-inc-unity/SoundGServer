using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace shooter_server
{
    public class Player
    {
        public int Id { get; set; }

        // Конструктор для инициализации объекта Player
        public Player(int id)
        {
            Id = id;
        }

        public Player()
        {
            Id = -1;
        }

        // Отправка сообщение клиенту, определенному вебсокету
        public async Task SendMessageAsync(WebSocket webSocket, string message)
        {
            try
            {
                Console.WriteLine(message);
                byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                ArraySegment<byte> segment = new ArraySegment<byte>(messageBytes, 0, messageBytes.Length);
                await webSocket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending message to client: {ex.Message}");
            }
        }
    }
}
