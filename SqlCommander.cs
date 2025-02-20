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
                Console.WriteLine($"R AA");
                // SendMessage requestId login password
                List<string> credentials = new List<string>(sqlCommand.Split(' '));

                credentials.RemoveAt(0);

                int requestId = int.Parse(credentials[0]);
                byte[] login = Convert.FromBase64String(credentials[1]);
                byte[] password = Convert.FromBase64String(credentials[2]);

                Console.WriteLine($"R A");

                // Check if the login already exists
                using (var checkCmd = dbConnection.CreateCommand())
                {
                    checkCmd.CommandText = "SELECT COUNT(*) FROM Anon WHERE Login = @login";
                    checkCmd.Parameters.AddWithValue("login", login);

                    int count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

                    Console.WriteLine($"R B");

                    if (count > 0)
                    {
                        // Login already exists
                        lobby.SendMessagePlayer($"User with this login already exists", ws, requestId);
                    }
                    else
                    {
                        // Insert new record
                        using (var insertCmd = dbConnection.CreateCommand())
                        {
                            insertCmd.CommandText = "INSERT INTO Anon (Login, Password) VALUES (@login, @password)";
                            insertCmd.Parameters.AddWithValue("login", login);
                            insertCmd.Parameters.AddWithValue("password", password);

                            await insertCmd.ExecuteNonQueryAsync();

                            // Registration successful
                            lobby.SendMessagePlayer($"true", ws, requestId);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error SendMessage command: {e}");
            }
        }

    }
}
