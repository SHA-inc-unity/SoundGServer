using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace shooter_server
{
    public class Lobby
    {
        // Все пользователи, которые на сервере, подключенные по вебсокету
        private Dictionary<WebSocket, Player> players = new Dictionary<WebSocket, Player>();

        public SqlCommander SqlCommander = new SqlCommander(
                "localhost",
                "postgres",
                "postgres",
                "teletypedb",
                5432
            );

        // Можно получить, но нельзя отправить пользователю
        public Dictionary<WebSocket, Player> Players { get => players; }

        // Всем.
        public async void SendMessageAll(string message)
        {
            foreach (var player in Players)
            {
                await player.Value.SendMessageAsync(player.Key, message);
            }
        }

        // Всем кому не введен вебсокет
        public async void SendMessageExcept(string message, WebSocket ws)
        {
            foreach (var player in Players)
            {
                if (player.Key != ws)
                {
                    await player.Value.SendMessageAsync(player.Key, message);
                }
            }
        }

        // Отправка клиенту, чей вебсокет был введен
        public async void SendMessagePlayer(string message, WebSocket ws, int idRequest)
        {
            Console.WriteLine(message);
            await Players[ws].SendMessageAsync(ws, idRequest.ToString() + " " + message);
        }

        // 
        public virtual void AddPlayer(WebSocket ws, Player player)
        {
            if (Players.ContainsKey(ws))
                Players[ws] = player;
            else
                Players.Add(ws, player);
        }

        // Удалить плеера из жизни
        public void RemovePlayer(WebSocket ws)
        {
            if (Players.ContainsKey(ws))
                Players.Remove(ws);
        }
    }
}