using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.Data.Sqlite;

namespace CinemaTelegramBot;

class Program
{
    static Dictionary<long, UserDialog> _userDialogs = new Dictionary<long, UserDialog>();

    static async Task Main(string[] args)
    {
        var token = Environment.GetEnvironmentVariable("BOT_TOKEN");
        if (string.IsNullOrEmpty(token))
        {
            token = "8805092991:AAG4PmEk3KhNr8WTFH8OmJ3lZ_N7iCkpRLg";
            Console.WriteLine("Використовується токен з коду (локальний режим)");
        }
        else
        {
            Console.WriteLine("Токен отримано зі змінної середовища (Railway)");
        }

        var botClient = new TelegramBotClient(token);
        using var cts = new CancellationTokenSource();

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>()
        };

        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            Console.WriteLine("Отримано сигнал завершення, зупиняємо бота...");
            cts.Cancel();
        };

        botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: cts.Token
        );

        Console.WriteLine("Бот запущено. Працює 24/7 на Railway!");

        await Task.Delay(-1, cts.Token);
    }

    static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Помилка: {exception.Message}");
        return Task.CompletedTask;
    }

    static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is not { } message)
            return;
        if (message.Text is not { } messageText)
            return;

        var chatId = message.Chat.Id;

        if (messageText == "/start")
        {
            var replyKeyboard = new ReplyKeyboardMarkup(new[]
            {
                new[] { new KeyboardButton("Фільми") },
                new[] { new KeyboardButton("Сеанси") },
                new[] { new KeyboardButton("Додати фільм") }
            })
            { ResizeKeyboard = true };
            await botClient.SendMessage(chatId, "Вітаю в кінотеатрі!\nОберіть дію:", replyMarkup: replyKeyboard, cancellationToken: cancellationToken);
            return;
        }

        if (messageText == "Фільми")
        {
            var movies = GetMoviesFromDb();
            if (movies.Count == 0)
            {
                await botClient.SendMessage(chatId, "Фільмів поки немає.", cancellationToken: cancellationToken);
                return;
            }

            var response = "*Список фільмів:*\n";
            foreach (var m in movies)
                response += $"• `{m.Id}`. {m.Title} ({m.Genre}, {m.DurationMinutes} хв)\n";

            await botClient.SendMessage(chatId, response, parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
            return;
        }

        if (messageText == "Сеанси")
        {
            var sessions = GetSessionsFromDb();
            if (sessions.Count == 0)
            {
                await botClient.SendMessage(chatId, "Сеансів поки немає.", cancellationToken: cancellationToken);
                return;
            }

            var response = "*Список сеансів:*\n";
            foreach (var s in sessions)
                response += $"• {s.MovieTitle} — {s.DateTime:dd.MM.yyyy HH:mm} — {s.Price} грн\n";

            await botClient.SendMessage(chatId, response, parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
            return;
        }

        if (messageText == "Додати фільм")
        {
            _userDialogs[chatId] = new UserDialog { Step = DialogStep.AwaitingMovieTitle };
            await botClient.SendMessage(chatId, "Введіть назву фільму:", cancellationToken: cancellationToken);
            return;
        }

        if (_userDialogs.TryGetValue(chatId, out var dialog))
        {
            await ProcessMovieCreationDialog(botClient, chatId, messageText, dialog, cancellationToken);
            return;
        }

        await botClient.SendMessage(chatId, "Невідома команда. Використовуйте меню.", cancellationToken: cancellationToken);
    }

    static async Task ProcessMovieCreationDialog(ITelegramBotClient botClient, long chatId, string input, UserDialog dialog, CancellationToken cancellationToken)
    {
        switch (dialog.Step)
        {
            case DialogStep.AwaitingMovieTitle:
                dialog.MovieTitle = input;
                dialog.Step = DialogStep.AwaitingMovieGenre;
                await botClient.SendMessage(chatId, "Введіть жанр фільму:", cancellationToken: cancellationToken);
                break;
            case DialogStep.AwaitingMovieGenre:
                dialog.MovieGenre = input;
                dialog.Step = DialogStep.AwaitingMovieDuration;
                await botClient.SendMessage(chatId, "Введіть тривалість (у хвилинах):", cancellationToken: cancellationToken);
                break;
            case DialogStep.AwaitingMovieDuration:
                if (int.TryParse(input, out int duration))
                {
                    AddMovieToDb(dialog.MovieTitle, dialog.MovieGenre, duration);
                    await botClient.SendMessage(chatId, $"Фільм \"{dialog.MovieTitle}\" додано!", cancellationToken: cancellationToken);
                    _userDialogs.Remove(chatId);
                }
                else
                {
                    await botClient.SendMessage(chatId, "Тривалість має бути числом. Спробуйте ще раз:", cancellationToken: cancellationToken);
                }
                break;
        }
    }

    static void AddMovieToDb(string title, string genre, int duration)
    {
        using var connection = new SqliteConnection("Data Source=C:\\Users\\user\\Desktop\\.NET\\5\\CinemaBlazor\\CinemaDatabase.db");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"INSERT INTO Movies (Title, Genre, DurationMinutes, Director, AgeRestriction) 
                            VALUES (@title, @genre, @duration, @director, @ageRestriction)";
        command.Parameters.AddWithValue("@title", title);
        command.Parameters.AddWithValue("@genre", genre);
        command.Parameters.AddWithValue("@duration", duration);
        command.Parameters.AddWithValue("@director", "Невідомий");
        command.Parameters.AddWithValue("@ageRestriction", 0);
        command.ExecuteNonQuery();
    }

    static List<MovieModel> GetMoviesFromDb()
    {
        var movies = new List<MovieModel>();
        using var connection = new SqliteConnection("Data Source=C:\\Users\\user\\Desktop\\.NET\\5\\CinemaBlazor\\CinemaDatabase.db");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Title, Genre, DurationMinutes FROM Movies";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            movies.Add(new MovieModel
            {
                Id = reader.GetInt32(0),
                Title = reader.GetString(1),
                Genre = reader.GetString(2),
                DurationMinutes = reader.GetInt32(3)
            });
        }
        return movies;
    }

    static List<SessionModel> GetSessionsFromDb()
    {
        var sessions = new List<SessionModel>();
        using var connection = new SqliteConnection("Data Source=CinemaDatabase.db");
        connection.Open();

        var tableCheckCmd = connection.CreateCommand();
        tableCheckCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='Sessions'";
        var tableExists = tableCheckCmd.ExecuteScalar() != null;

        if (!tableExists)
        {
            return sessions;
        }

        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT s.Id, m.Title, s.DateTime, s.BasePrice 
                                FROM Sessions s 
                                JOIN Movies m ON m.Id = s.MovieId";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            sessions.Add(new SessionModel
            {
                Id = reader.GetInt32(0),
                MovieTitle = reader.GetString(1),
                DateTime = reader.GetDateTime(2),
                Price = reader.GetDecimal(3)
            });
        }
        return sessions;
    }
}

public class MovieModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
}

public class SessionModel
{
    public int Id { get; set; }
    public string MovieTitle { get; set; } = string.Empty;
    public DateTime DateTime { get; set; }
    public decimal Price { get; set; }
}

public enum DialogStep { AwaitingMovieTitle, AwaitingMovieGenre, AwaitingMovieDuration }

public class UserDialog
{
    public DialogStep Step { get; set; }
    public string MovieTitle { get; set; } = string.Empty;
    public string MovieGenre { get; set; } = string.Empty;
}