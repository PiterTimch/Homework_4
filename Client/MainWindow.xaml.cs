using Microsoft.Win32;
using Newtonsoft.Json;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Client
{
    //клас для відправки фото на сервер у json форматі
    public class UploadImage 
    {
        public string Image { get; set; } //шлях до фото
    }

    public partial class MainWindow : Window
    {
        private string _serverUrl = "https://kukumber.itstep.click"; //посилання на сервер зберігання зображень
        private string _userImage; //аватарка користувача
        private ChatMessage _message = new ChatMessage(); //повідомлення, що відправляється
        private TcpClient _tcpClient = new TcpClient(); //клієнт для зв'язку із сервером чату
        private NetworkStream _ns; //потік для передачі даних через мережу
        private Thread _thread; // окремий потік

        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Обробник події закриття вікна. Виконує коректне завершення роботи клієнта:
        /// відправляє на сервер повідомлення про вихід, закриває потік і з'єднання.
        /// </summary>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _message.Text = "Покинув чат";

            // Серіалізуємо повідомлення
            var buffer = _message.Serialize();

            // Відправляємо повідомлення на сервер
            _ns.Write(buffer);

            // Коректно завершуємо з'єднання
            _tcpClient.Client.Shutdown(SocketShutdown.Both);
            _tcpClient.Close();
        }

        /// <summary>
        /// Обробник події натискання кнопки "Вибрати фото".
        /// Дозволяє користувачу обрати зображення з локального диска, 
        /// конвертує його у формат Base64, відправляє на сервер і отримує посилання на завантажене зображення.
        /// </summary>
        private void btnPhotoSelect_Click(object sender, RoutedEventArgs e)
        {
            //отримання локального шляху до файлу через діалогове вікно
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.ShowDialog();
            var filePath = dlg.FileName;

            //конвертація зображення в base64 для відправки на сервер через JSON
            var bytes = File.ReadAllBytes(filePath);
            var base64 = Convert.ToBase64String(bytes);
            string json = JsonConvert.SerializeObject(new
            {
                photo = base64,
            });
            bytes = Encoding.UTF8.GetBytes(json);

            //запит на сервер для завантаження фото
            WebRequest request = WebRequest.Create($"{_serverUrl}/api/galleries/upload");
            request.Method = "POST";
            request.ContentType = "application/json";

            //відправка повідомлення на сервер
            using (var stream = request.GetRequestStream())
            {
                stream.Write(bytes, 0, bytes.Length);
            }

            try
            {
                //отримання відповіді від сервера
                var response = request.GetResponse();
                using (var stream = new StreamReader(response.GetResponseStream()))
                {
                    //зчитування та десереалізація повідомлення
                    string data = stream.ReadToEnd();
                    var resp = JsonConvert.DeserializeObject<UploadImage>(data);
                    MessageBox.Show(_serverUrl + resp.Image);
                    if (resp != null)
                        _userImage = resp.Image;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        /// <summary>
        /// Відображає повідомлення.
        /// Створює новий елемент для повідомлення з текстом і зображенням користувача.
        /// </summary>
        /// <param name="text">Текст повідомлення.</param>
        /// <param name="imageUrl">URL-адреса зображення користувача.</param>
        private void ViewMessage(string text, string imageUrl)
        {
            //створнення грід-розмітки
            var grid = new Grid();
            for (int i = 0; i < 2; i++)
            {
                var colDef = new ColumnDefinition();
                colDef.Width = GridLength.Auto;
                grid.ColumnDefinitions.Add(colDef);
            }

            //налаштування аватарки
            BitmapImage bmp = new BitmapImage(new Uri($"{_serverUrl}{imageUrl}"));
            var image = new Image();
            image.Source = bmp;
            image.Width = 50;
            image.Height = 50;

            //налантування тексту повідомлення
            var textBlock = new TextBlock();
            Grid.SetColumn(textBlock, 1);
            textBlock.VerticalAlignment = VerticalAlignment.Center;
            textBlock.Margin = new Thickness(5, 0, 0, 0);
            textBlock.Text = text;

            grid.Children.Add(image);
            grid.Children.Add(textBlock);

            lbInfo.Items.Add(grid);//додання повідомлення до чату

            //прокрутка чату до надісланого повідомлення
            lbInfo.Items.MoveCurrentToLast();
            lbInfo.ScrollIntoView(lbInfo.Items.CurrentItem);
        }

        /// <summary>
        /// Обробник події для кнопки підключення до чату.
        /// Перевіряє введені дані, встановлює з'єднання з сервером і надсилає інформацію про користувача.
        /// </summary>
        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            //валідація даних
            if (string.IsNullOrEmpty(_userImage))
            {
                MessageBox.Show("Оберіть фото для корристувача");
                return;
            }
            if (string.IsNullOrEmpty(txtUserName.Text))
            {
                MessageBox.Show("Вкажіть назву користувача");
                return;
            }

            try
            {
                //налаштування сервера
                IPAddress ip = IPAddress.Parse("192.168.0.104");
                int port = 4412;

                //налаштування повідомлення перед відправкою
                _message.UserId = Guid.NewGuid().ToString();//встановлення унікального ідентифікатора
                _message.Name = txtUserName.Text;
                _message.Photo = _userImage;

                //підключення до сервера
                _tcpClient.Connect(ip, port);
                _ns = _tcpClient.GetStream();

                // Запуск окремого потоку для обробки відповідей від сервера
                _thread = new Thread(obj => ResponseData((TcpClient)obj));
                _thread.Start(_tcpClient);

                //обмеження кнопок клієнта
                btnSend.IsEnabled = true;
                btnConnect.IsEnabled = false;
                txtUserName.IsEnabled = false;

                // Надсилання повідомлення про приєднання до чату
                _message.Text = "Приєднався до чату";
                var buffer = _message.Serialize();
                _ns.Write(buffer); 

            }
            catch (Exception ex)
            {
                MessageBox.Show("Проблема підключення до серверу " + ex.Message);
            }
        }

        /// <summary>
        /// Метод для обробки даних, отриманих від сервера.
        /// Читає повідомлення у потоці, десеріалізує їх і відображає у вікні чату.
        /// </summary>
        /// <param name="client">TCP-клієнт, який використовує з'єднання.</param>
        private void ResponseData(TcpClient client)
        {
            //отримання потоку для читання
            NetworkStream ns = client.GetStream();

            //буфер для повідомлень
            byte[] readBytes = new byte[16054400];
            int byte_count;

            //читаємо відповідь від сервера, поки надходять дані
            while ((byte_count = ns.Read(readBytes)) > 0)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    //десеріалізація для потоку та вивід повідомлення
                    ChatMessage msg = ChatMessage.Desserialize(readBytes);
                    string text = $"{msg.Name} -> {msg.Text}";
                    ViewMessage(text, msg.Photo);

                }));
            }
        }

        /// <summary>
        /// Обробник події натискання кнопки "Відправити".
        /// Відправляє повідомлення, введене користувачем, на сервер.
        /// </summary>
        private void btnSend_Click(object sender, RoutedEventArgs e)
        {
            _message.Text = txtText.Text;
            var buf = _message.Serialize();//серіалізація повідомлення

            //відправка даних на сервер
            _ns.Write(buf);

            //очищення поля вводу
            txtText.Text = "";
        }
    }
}