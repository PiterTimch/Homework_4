using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks.Dataflow;

namespace Lesson_4.Server
{
    class Program // клас ініціалізації додатку програми
    {
        static readonly object _lock = new object(); //поле для блокування потоку
        static readonly Dictionary<int, TcpClient> list_clients = new Dictionary<int, TcpClient>(); //словник користувачів, де ключ - це Id

        static async Task Main(string[] args) 
        {
            int clientCounetr = 1; //кількість клієнтів

            //налаштування кодування консолі
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;

            //отримання данних про хост для витягу можливих ip
            var hostName = Dns.GetHostName();
            Console.WriteLine($"Мій хост {hostName}");
            var localhost = await Dns.GetHostEntryAsync(hostName);

            //вибір ip за замовчуванням
            int count = localhost.AddressList.Length;
            var ip = localhost.AddressList[count - 1];
            int i = 0;
            Console.WriteLine("Вкажіть IP адресу:");
            foreach (var item in localhost.AddressList)
            {
                Console.WriteLine($"{++i}.{item}");
            }
            Console.Write($"({ip})->_");
            var temp = Console.ReadLine();
            if (!string.IsNullOrEmpty(temp))
                ip = IPAddress.Parse(temp);
            int port = 4412; //порт сервера
            
            Console.Title = ($"Ваш IP {ip} :)");

            //ініціалізація та запуск сервера
            TcpListener serverSocket = new TcpListener(ip, port);
            serverSocket.Start();
            Console.WriteLine("Run!");

            //цикл обробки запитів
            while (true)
            {
                TcpClient client = serverSocket.AcceptTcpClient(); //отримання клієнту
                lock (_lock) 
                {
                    list_clients.Add(clientCounetr, client); //додавання клієнта до словника клієнтів
                }

                Console.WriteLine($"{clientCounetr}: {client.Client.RemoteEndPoint}");

                //запуск обробки запиту в окремому потоці
                Thread t = new Thread(HandleClient);
                t.Start(clientCounetr);

                clientCounetr++;
            }
        }


        /// <summary>
        /// Обробляє підключення клієнта до сервера.
        /// Забезпечує отримання та обробку даних, надісланих клієнтом, а також їх передачу всім іншим клієнтам.
        /// </summary>
        /// <param name="clientCounetr">
        /// Ідентифікатор клієнта в списку клієнтів. Очікується, що передане значення є `int`.
        /// </param>
        /// <remarks>
        /// 1. **Синхронізація:** Використовується `lock` для роботи зі списком клієнтів `list_clients`, 
        ///    щоб уникнути проблем багатопотоковості при доступі до загальних ресурсів.
        /// 2. **Обробка винятків:** 
        ///    Метод використовує конструкцію `try-catch-finally`, щоб гарантувати безпечне завершення роботи клієнта 
        ///    та звільнення ресурсів навіть у разі виникнення помилок.
        /// 3. **Обмеження:** 
        ///    - Максимальний розмір буфера даних обмежений 10240 байтами. Це може викликати проблеми при передачі великих повідомлень.
        ///    - Поточна реалізація не підтримує розбиття даних на фрагменти, що може призвести до втрати даних у разі великого трафіку.
        /// </remarks>
        public static void HandleClient(object clientCounetr) 
        {
            int id = (int)clientCounetr;
            TcpClient client;
            lock (_lock) 
            {
                client = list_clients[id]; //отримання клієнта
            }
            try
            {
                while (true) 
                {
                    NetworkStream stream = client.GetStream(); //отримання потоку даних від клієнта
                    
                    //зчитування надісланих даних
                    byte[] buffer = new byte[10240];

                    int byteCount = stream.Read(buffer);

                    if (byteCount == 0) 
                    {
                        break;
                    }

                    //перетворення надісланих даних у стрічку
                    string data = Encoding.UTF8.GetString(buffer, 0, byteCount);

                    //розсилка повідомлення усім клієнтам
                    broadcast(data);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex}");
            }
            finally 
            {
                //відключення від сервера
                client.Client.Shutdown(SocketShutdown.Both);
                client.Close();
            }
            lock (_lock)
            {
                Console.WriteLine("Чат покинуто");
                list_clients.Remove(id);
            }
        }


        /// <summary>
        /// Надсилає повідомлення всім клієнтам, підключеним до сервера.
        /// </summary>
        /// <param name="data">
        /// Текстове повідомлення, яке потрібно відправити. Повинно бути не null і закодоване у форматі UTF-8.
        /// </param>
        /// <remarks>
        /// 1. **Синхронізація:**
        ///    - Використовується `lock (_lock)` для забезпечення потокобезпеки при доступі до списку клієнтів `list_clients`.
        ///
        /// 2. **Кодування:**
        ///    - Вхідний рядок перетворюється у байти за допомогою кодування UTF-8. Це стандартне кодування, що підтримує більшість мов світу.
        ///
        /// 3. **Можливі проблеми:**
        ///    - Якщо клієнт недоступний або його потік неактивний, може виникнути виняток. 
        ///    - Поточна реалізація не виключає з обробки недоступних клієнтів.
        /// </remarks>
        private static void broadcast(string data)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(data);

            lock (_lock) 
            {
                try
                {
                    foreach (var c in list_clients.Values)
                    {
                        //надсилання кожному клієнту в потік повідомлення у вигляді байтів
                        NetworkStream stream = c.GetStream();
                        stream.Write(bytes);
                    }
                }
                catch (Exception ex)
                {

                    throw;
                }
            }
        }
    }
}