using MongoDB.Bson;
using MongoDB.Driver;
using Syncfusion.Windows.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MySmartHouse
{
    public partial class History : Window
    {
        private MongoClient client;
        private IMongoCollection<BsonDocument> greenhouse;

        public History()
        {
            InitializeComponent();

            const string connectionUrl = "mongodb://localhost:27017";
            client = new MongoClient(connectionUrl);
            IMongoDatabase database = client.GetDatabase("greenhouse");
            greenhouse = database.GetCollection<BsonDocument>("greenhouse");
        }

        private async void OK_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Sensors_ComboBox.SelectedItem != null && Start_Date.SelectedDate != null && End_Date.SelectedDate != null)
            {
                string selectedSensor = ((ComboBoxItem)Sensors_ComboBox.SelectedItem).Content.ToString();
                DateTime startDate = Start_Date.SelectedDate.Value;
                DateTime endDate = End_Date.SelectedDate.Value;

                // Фильтр для получения исторических данных
                var filter = Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Gte("timestamp", startDate),
                    Builders<BsonDocument>.Filter.Lte("timestamp", endDate)
                );

                // Проекция для получения массива "history"
                var projection = Builders<BsonDocument>.Projection.Include("history");

                // Получение документов, соответствующих фильтру и проекции
                var documents = await greenhouse.Find(filter).Project(projection).ToListAsync();

                // Определение списка исторических данных
                List<BsonDocument> historyList = new List<BsonDocument>();

                foreach (var doc in documents)
                {
                    if (doc.Contains("history"))
                    {
                        var historyArray = doc["history"].AsBsonArray;
                        historyList.AddRange(historyArray.Select(x => x.AsBsonDocument));
                    }
                }

                // Фильтрация по времени на клиенте и выбор нужных данных
                var filteredHistory = historyList
                    .Where(doc => doc.Contains("timestamp") && doc["timestamp"].ToUniversalTime() >= startDate && doc["timestamp"].ToUniversalTime() <= endDate)
                    .Select(doc => new
                    {
                        Date = doc["timestamp"].ToUniversalTime(),
                        Value = doc["sensor_values"][selectedSensor].ToInt32()
                    })
                    .ToList();

                // Вывод данных в History_List
                History_List.ItemsSource = filteredHistory;

                Console.WriteLine($"Количество записей: {filteredHistory.Count}");
                foreach (var item in filteredHistory)
                {
                    Console.WriteLine($"Дата: {item.Date}, Значение: {item.Value}");
                }
            }
            else
            {
                Console.WriteLine("Пожалуйста, выберите дату и предмет.");
            }
        }


    }
}
