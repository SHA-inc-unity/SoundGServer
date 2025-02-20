using System.Text;
using System.Net.WebSockets;
using Npgsql;
using System.Security.Cryptography;
using System.Data;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Specialized;
using System.Globalization;
using System.Numerics;
using System.Diagnostics;
using System.Net;
using System.ComponentModel;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text.Json;


namespace shooter_server
{
    public class SqlCommander
    {
        private string host;
        private string user;
        private string password;
        private string database;
        private int port;


        public SqlCommander(string host, string user, string password, string database, int port)
        {
            this.host = host;
            this.user = user;
            this.password = password;
            this.database = database;
            this.port = port;
        }


        public async Task ExecuteSqlCommand(Lobby lobby, WebSocket webSocket, string sqlCommand, Player player)
        {
            // Создание соединения с базой данных
            using (var dbConnection = new NpgsqlConnection($"Host={host};Username={user};Password={password};Database={database};Port={port}"))
            {
                await dbConnection.OpenAsync();
                //Console.WriteLine(dbConnection.ConnectionString);

                int senderId = player.Id;

                if (dbConnection.State != ConnectionState.Open)
                {
                    Console.WriteLine("DB connection error");

                    return;
                }

                //Console.WriteLine(sqlCommand);

                try
                {
                    // Определение типа SQL-команды
                    switch (sqlCommand)
                    {
                        case string s when s.StartsWith("Login"):
                            //OK
                            await Task.Run(() => Login(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        case string s when s.StartsWith("Register"):
                            //OK
                            await Task.Run(() => Register(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        case string s when s.StartsWith("GetTopSongs"):
                            //OK
                            await Task.Run(() => GetTopSongs(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        default:
                            Console.WriteLine("Command not found");
                            break;
                    }
                }
                catch (Exception e)
                {
                    //Console.WriteLine($"Error executing SQL command: {e}");
                }
            }
        }


        private async Task Login(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                // Разбираем команду
                List<string> credentials = new List<string>(sqlCommand.Split(' '));
                credentials.RemoveAt(0); // Убираем "Login"

                int requestId = int.Parse(credentials[0]);
                string login = credentials[1];
                string hashedPassword = credentials[2];

                using (var cursor = dbConnection.CreateCommand())
                {
                    cursor.CommandText = @"
                SELECT 
                    UserName 
                FROM 
                    UserTable 
                WHERE 
                    UserName = @login AND Password = @password";

                    cursor.Parameters.AddWithValue("login", login);
                    cursor.Parameters.AddWithValue("password", hashedPassword);

                    using (var reader = await cursor.ExecuteReaderAsync())
                    {
                        if (reader.Read())
                        {
                            string userName = reader.GetString(0);

                            // Отправляем успешный логин
                            string result = $"true {userName}";
                            lobby.SendMessagePlayer(result, ws, requestId);
                        }
                        else
                        {
                            // Если логин или пароль неверны
                            string result = "false Invalid login or password";
                            lobby.SendMessagePlayer(result, ws, requestId);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error in Login command: {e}");
            }
        }

        private async Task Register(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                // Разбираем команду
                List<string> credentials = new List<string>(sqlCommand.Split(' '));
                credentials.RemoveAt(0); // Убираем "Register"

                int requestId = int.Parse(credentials[0]);
                string login = credentials[1];
                string email = credentials[2];
                string hashedPassword = credentials[3];

                using (var cursor = dbConnection.CreateCommand())
                {
                    cursor.CommandText = @"
                INSERT INTO UserTable (UserName, Password, EmailAddress) 
                VALUES (@login, @password, @email)";

                    cursor.Parameters.AddWithValue("login", login);
                    cursor.Parameters.AddWithValue("password", hashedPassword);
                    cursor.Parameters.AddWithValue("email", email);

                    try
                    {
                        await cursor.ExecuteNonQueryAsync();

                        // Успешная регистрация
                        string result = "true Registration successful";
                        lobby.SendMessagePlayer(result, ws, requestId);
                    }
                    catch (PostgresException ex) when (ex.SqlState == "23505") // Код ошибки уникальности
                    {
                        // Логин или email уже существуют
                        string result = "false UserName or Email already exists";
                        lobby.SendMessagePlayer(result, ws, requestId);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error in Register command: {e}");
            }
        }


        private async Task GetTopSongs(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                // Разбираем входные данные
                List<string> credentials = new List<string>(sqlCommand.Split(' '));
                credentials.RemoveAt(0); // Убираем "GetTopSongs"

                int requestId = int.Parse(credentials[0]);
                string login = credentials[1];
                string hashedPassword = credentials[2];

                // Проверяем логин и пароль
                string userQuery = @"
            SELECT UserName 
            FROM UserTable 
            WHERE UserName = @login AND Password = @password";

                using (var userCmd = dbConnection.CreateCommand())
                {
                    userCmd.CommandText = userQuery;
                    userCmd.Parameters.AddWithValue("login", login);
                    userCmd.Parameters.AddWithValue("password", hashedPassword);

                    using (var reader = await userCmd.ExecuteReaderAsync())
                    {
                        if (!reader.Read())
                        {
                            string result = "false Invalid login or password";
                            lobby.SendMessagePlayer(result, ws, requestId);
                            return;
                        }
                    }
                }

                // Получаем топ-100 песен по BuyCount, а также проверяем их статус для пользователя
                string topSongsQuery = @"
            SELECT s.SongName, s.Price, s.BuyCount, 
                   COALESCE(ut.OwnType, 'load') AS OwnType
            FROM Songs s
            LEFT JOIN UserToSong ut ON s.SongName = ut.SongName AND ut.UserName = @login
            ORDER BY s.BuyCount DESC
            LIMIT 100";

                List<string> topSongs = new List<string>();

                using (var topSongCmd = dbConnection.CreateCommand())
                {
                    topSongCmd.CommandText = topSongsQuery;
                    topSongCmd.Parameters.AddWithValue("login", login);

                    using (var topSongReader = await topSongCmd.ExecuteReaderAsync())
                    {
                        while (topSongReader.Read())
                        {
                            string songName = topSongReader.GetString(0);
                            int price = topSongReader.GetInt32(1);
                            int buyCount = topSongReader.GetInt32(2);
                            string ownType = topSongReader.GetString(3); // buyed, owner, или load
                            topSongs.Add($"{songName}_{price}_{buyCount}_{ownType}");
                        }
                    }
                }

                // Формируем итоговый список
                string finalResult = $"true {string.Join(" ", topSongs)}";
                lobby.SendMessagePlayer(finalResult, ws, requestId);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error in GetTopSongs command: {e}");
            }
        }


    }
}
