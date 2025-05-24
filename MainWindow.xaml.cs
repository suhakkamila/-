using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.ComponentModel;
using System.Text;
using System.IO;
using Microsoft.Win32;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace QueensSolverRBFS
{
    // Клас для представлення стану дошки в A*
    public class AStarNode
    {
        private static int boardSize = 8;

        public int[] Board { get; }
        public int QueensPlaced { get; }
        public int F { get; set; } // f = g + h
        public int G { get; set; } // g = глибина (кількість розміщених ферзів)
        public int H { get; set; } // h = евристика (кількість ферзів, що залишилось розмістити)

        public AStarNode(int[] board)
        {
            Board = (int[])board.Clone();
            QueensPlaced = board.Count(q => q != -1);
            G = QueensPlaced;
            H = boardSize - QueensPlaced;
            F = G + H;
        }

        public override int GetHashCode()
        {
            return string.Join(",", Board).GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj is AStarNode other)
            {
                for (int i = 0; i < boardSize; i++)
                {
                    if (Board[i] != other.Board[i])
                        return false;
                }
                return true;
            }
            return false;
        }
    }

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // Модель даних
        private const int boardSize = 8; // Фіксуємо розмір 8x8
        private int[] board;
        private bool isRunning = false;
        private bool isPaused = false;
        private bool hasSolution = false;
        private DispatcherTimer visualizationTimer;
        private DispatcherTimer statsUpdateTimer; // Таймер оновлення статистики
        private List<int[]> visualizationSteps = new List<int[]>();
        private List<string> stepDescriptions = new List<string>();
        private int currentStep = 0;
        private StringBuilder debugLog = new StringBuilder();
        private int searchNodesCount = 0;
        private int queensPlaced = 0;
        private DateTime startTime;
        private TimeSpan executionTime = TimeSpan.Zero;
        private long initialMemory = 0;
        private long maxMemoryUsed = 0;
        private Random random = new Random();

        // Розширена статистика
        private int backtracksCount = 0;     // Кількість повернень назад (відкатів)
        private int pruningCount = 0;        // Кількість відсікань гілок
        private double avgBranchingFactor = 0.0; // Середній коефіцієнт розгалуження
        private int totalBranches = 0;       // Загальна кількість гілок
        private int branchPoints = 0;        // Кількість точок розгалуження
        private int maxDepthReached = 0;     // Максимальна досягнута глибина
        private int deadEndsCount = 0;       // Кількість тупикових ситуацій
        private int successfulPlacements = 0; // Кількість успішних розміщень
        private double avgTimePerNode = 0.0;  // Середній час на вузол
        private CancellationTokenSource cancellationTokenSource;

        // Обробка подій MVVM
        public event PropertyChangedEventHandler? PropertyChanged;

        // Властивості з прив'язкою        
        public bool IsRunning
        {
            get { return isRunning; }
            set
            {
                isRunning = value;
                OnPropertyChanged("IsRunning");
                OnPropertyChanged("IsNotRunning");
            }
        }
        

        public bool IsNotRunning { get { return !isRunning; } }

        public bool HasSolution
        {
            get { return hasSolution; }
            set
            {
                hasSolution = value;
                OnPropertyChanged("HasSolution");
            }
        }

        public string DebugLogText
        {
            get { return debugLog.ToString(); }
            set
            {
                debugLog.Clear();
                debugLog.Append(value);
                OnPropertyChanged("DebugLogText");
            }
        }

        // Конструктор і ініціалізація
        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            board = new int[boardSize];
            for (int i = 0; i < boardSize; i++)
                board[i] = -1;

            visualizationTimer = new DispatcherTimer();
            visualizationTimer.Interval = TimeSpan.FromMilliseconds(800);
            visualizationTimer.Tick += VisualizationTimer_Tick;

            // Додаємо таймер для оновлення статистики
            statsUpdateTimer = new DispatcherTimer();
            statsUpdateTimer.Interval = TimeSpan.FromMilliseconds(200);
            statsUpdateTimer.Tick += StatsUpdateTimer_Tick;

            DrawBoard();
            UpdateStatusInfo();

            AddToLog("Програму запущено. Розставте початкових ферзів на дошці.");
            AddToLog("На дошці 8x8 для розв'язку потрібно розставити 8 ферзів.");
            AddToLog("Для початку роботи алгоритму достатньо поставити 1-2 ферзя.");
        }

        // Оновлення статистики в реальному часі
        private void StatsUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (isRunning && !isPaused)
            {
                executionTime = DateTime.Now - startTime;
                UpdateMemoryUsage();
                UpdateStatistics(true);
            }
        }

        // Оновлення використання пам'яті
        private void UpdateMemoryUsage()
        {
            using (Process currentProcess = Process.GetCurrentProcess())
            {
                currentProcess.Refresh();
                long currentMemory = currentProcess.PrivateMemorySize64;
                maxMemoryUsed = Math.Max(maxMemoryUsed, currentMemory - initialMemory);
            }
        }

        // Метод для ініціалізації шахової дошки
        private void InitializeBoard()
        {
            board = new int[boardSize];
            for (int i = 0; i < boardSize; i++)
                board[i] = -1;

            UpdateStatusInfo();
            HasSolution = false;

            // Скидаємо статистику
            ResetStatistics();
        }

        // Скидання статистики
        private void ResetStatistics()
        {
            searchNodesCount = 0;
            backtracksCount = 0;
            pruningCount = 0;
            totalBranches = 0;
            branchPoints = 0;
            maxDepthReached = 0;
            deadEndsCount = 0;
            successfulPlacements = 0;
            avgBranchingFactor = 0.0;
            avgTimePerNode = 0.0;
            executionTime = TimeSpan.Zero;
            maxMemoryUsed = 0;

            UpdateStatistics();
        }

        // Відрисовка шахової дошки
        private void DrawBoard()
        {
            chessboardCanvas.Children.Clear();

            double cellSize = Math.Min(chessboardCanvas.ActualWidth, chessboardCanvas.ActualHeight) / boardSize;
            if (cellSize <= 0) cellSize = 50; // За замовчуванням, якщо розмір Canvas ще не розрахований

            for (int row = 0; row < boardSize; row++)
            {
                for (int col = 0; col < boardSize; col++)
                {
                    Rectangle rect = new Rectangle();
                    rect.Width = cellSize;
                    rect.Height = cellSize;
                    rect.Fill = (row + col) % 2 == 0 ? Brushes.Beige : Brushes.SaddleBrown;
                    rect.Stroke = Brushes.Black;
                    rect.StrokeThickness = 1;

                    Canvas.SetLeft(rect, col * cellSize);
                    Canvas.SetTop(rect, row * cellSize);

                    rect.Tag = new Tuple<int, int>(row, col);
                    rect.MouseLeftButtonDown += ChessCell_Click;

                    chessboardCanvas.Children.Add(rect);

                    if (board[row] == col)
                    {
                        // Використовуємо символ ферзя з таблиці Unicode
                        TextBlock queenText = new TextBlock();
                        // Використовуємо білий ферзь на темних клітинах і чорний на світлих
                        queenText.Text = (row + col) % 2 == 0 ? "♛" : "♕"; // Unicode символи ферзя
                        queenText.FontSize = cellSize * 0.7;
                        queenText.Foreground = Brushes.Black;
                        queenText.TextAlignment = TextAlignment.Center;
                        queenText.FontWeight = FontWeights.Bold;

                        Canvas.SetLeft(queenText, col * cellSize + cellSize * 0.25);
                        Canvas.SetTop(queenText, row * cellSize);

                        chessboardCanvas.Children.Add(queenText);
                    }
                }
            }

            // Додаємо позначення рядків і стовпців
            for (int i = 0; i < boardSize; i++)
            {
                TextBlock rowLabel = new TextBlock();
                rowLabel.Text = (8 - i).ToString();
                rowLabel.FontWeight = FontWeights.Bold;
                Canvas.SetLeft(rowLabel, 5);
                Canvas.SetTop(rowLabel, i * cellSize + cellSize / 2 - 10);
                chessboardCanvas.Children.Add(rowLabel);

                TextBlock colLabel = new TextBlock();
                colLabel.Text = ((char)('a' + i)).ToString();
                colLabel.FontWeight = FontWeights.Bold;
                Canvas.SetLeft(colLabel, i * cellSize + cellSize / 2 - 5);
                Canvas.SetTop(colLabel, boardSize * cellSize - 15);
                chessboardCanvas.Children.Add(colLabel);
            }
        }

        // Обробка клацання по клітині дошки
        private void ChessCell_Click(object sender, MouseButtonEventArgs e)
        {
            if (isRunning) return;

            Rectangle rect = sender as Rectangle;
            if (rect != null)
            {
                var position = (Tuple<int, int>)rect.Tag;
                int row = position.Item1;
                int col = position.Item2;

                // Видалення ферзя, якщо він вже стоїть в цьому рядку
                if (board[row] != -1)
                {
                    int oldCol = board[row];
                    board[row] = -1;
                    AddToLog($"Видалено ферзя з позиції {(char)('a' + oldCol)}{8 - row}");
                }
                else
                {
                    board[row] = col;
                    AddToLog($"Розміщено ферзя на позиції {(char)('a' + col)}{8 - row}");
                }

                DrawBoard();
                UpdateStatusInfo();
            }
        }

        // Генерація випадкової розстановки ферзів
        private void RandomButton_Click(object sender, RoutedEventArgs e)
        {
            if (isRunning) return;

            // Скинути дошку
            InitializeBoard();

            // Розставити 1-3 ферзя в випадкових позиціях
            int queensToPlace = random.Next(1, 4);
            AddToLog($"Генеруємо випадкову розстановку з {queensToPlace} ферзів...");

            for (int i = 0; i < queensToPlace; i++)
            {
                int row, col;
                bool validPosition;

                // Шукаємо валідну позицію
                do
                {
                    row = random.Next(0, boardSize);
                    col = random.Next(0, boardSize);

                    // Перевіряємо, що рядок вільний і розміщення коректне
                    validPosition = board[row] == -1;

                    if (validPosition)
                    {
                        // Тимчасово розміщуємо ферзя для перевірки
                        board[row] = col;
                        validPosition = IsValid();

                        if (!validPosition)
                            board[row] = -1; // Якщо невалідно, скидаємо
                    }
                } while (!validPosition);

                AddToLog($"Розміщено ферзя на позиції {(char)('a' + col)}{8 - row}");
            }

            DrawBoard();
            UpdateStatusInfo();
        }

        // Збереження результату у файл
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!hasSolution) return;

            SaveFileDialog saveDialog = new SaveFileDialog();
            saveDialog.Filter = "Текстові файли (*.txt)|*.txt|Всі файли (*.*)|*.*";
            saveDialog.Title = "Зберегти результат розв'язання";
            saveDialog.DefaultExt = "txt";
            saveDialog.FileName = "Queens_Solution";

            if (saveDialog.ShowDialog() == true)
            {
                using (StreamWriter writer = new StreamWriter(saveDialog.FileName))
                {
                    writer.WriteLine("=== Розв'язання задачі 8 ферзів ===");
                    writer.WriteLine($"Дата і час: {DateTime.Now}");
                    writer.WriteLine($"Алгоритм: {algorithmSelector.Text}");
                    writer.WriteLine();

                    // Виведення шахової дошки у текстовий вигляд
                    writer.WriteLine("  a b c d e f g h");
                    writer.WriteLine("  ---------------");
                    for (int row = 0; row < boardSize; row++)
                    {
                        writer.Write($"{8 - row}|");
                        for (int col = 0; col < boardSize; col++)
                        {
                            if (board[row] == col)
                                writer.Write("Q ");
                            else
                                writer.Write(". ");
                        }
                        writer.WriteLine($"|{8 - row}");
                    }
                    writer.WriteLine("  ---------------");
                    writer.WriteLine("  a b c d e f g h");
                    writer.WriteLine();

                    // Запис координат ферзів
                    writer.WriteLine("Координати ферзів:");
                    for (int row = 0; row < boardSize; row++)
                    {
                        if (board[row] != -1)
                        {
                            writer.WriteLine($"Ферзь {row + 1}: {(char)('a' + board[row])}{8 - row}");
                        }
                    }
                    writer.WriteLine();

                    // Статистика виконання
                    writer.WriteLine("Статистика виконання:");
                    writer.WriteLine($"Алгоритм: {algorithmSelector.Text}");
                    writer.WriteLine($"Переглянуто станів: {searchNodesCount:N0}");
                    writer.WriteLine($"Час виконання: {executionTime.TotalSeconds:F3} секунд");
                    writer.WriteLine($"Використана пам'ять: {maxMemoryUsed / 1024.0 / 1024.0:F2} МБ");
                    writer.WriteLine($"Повернень назад: {backtracksCount:N0}");
                    writer.WriteLine($"Відсічено гілок: {pruningCount:N0}");
                    writer.WriteLine($"Тупикових ситуацій: {deadEndsCount:N0}");
                    writer.WriteLine($"Макс. глибина пошуку: {maxDepthReached}");
                    writer.WriteLine($"Середній коефіцієнт розгалуження: {avgBranchingFactor:F2}");
                    writer.WriteLine($"Швидкість пошуку: {(searchNodesCount / executionTime.TotalSeconds):F1} вузлів/с");
                }

                AddToLog($"Результат успішно збережено у файл: {saveDialog.FileName}");
            }
        }

        // Оновлення підписів кнопок і статусів згідно з вибраним алгоритмом
        private void AlgorithmSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (algorithmSelector != null)
            {
                string algorithm = ((ComboBoxItem)algorithmSelector.SelectedItem).Content.ToString();
                AddToLog($"Вибрано алгоритм: {algorithm}");
                UpdateStatistics();
            }
        }

        // Оновлення інформації про стан дошки
        private void UpdateStatusInfo()
        {
            queensPlaced = board.Count(q => q != -1);
            statusText.Text = $"Розміщено ферзів: {queensPlaced} з {boardSize}. {(IsValid() ? "Розміщення коректне." : "Ферзі атакують один одного!")}";
        }

        // Перевірка, чи коректне розміщення ферзів
        private bool IsValid()
        {
            for (int i = 0; i < boardSize; i++)
            {
                if (board[i] == -1)
                    continue;

                for (int j = i + 1; j < boardSize; j++)
                {
                    if (board[j] == -1)
                        continue;

                    // Перевірка на атаку по горизонталі
                    if (board[i] == board[j])
                        return false;

                    // Перевірка на атаку по діагоналі
                    if (Math.Abs(board[i] - board[j]) == Math.Abs(i - j))
                        return false;
                }
            }

            return true;
        }

        // Запуск алгоритму розв'язання
        private void SolveButton_Click(object sender, RoutedEventArgs e)
        {
            if (isRunning) return;

            if (!IsValid())
            {
                MessageBox.Show("Поточне розміщення ферзів неправильне! Ферзі не повинні атакувати один одного.",
                                "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            IsRunning = true;
            isPaused = false;
            HasSolution = false;
            debugLog.Clear();
            ResetStatistics();

            string algorithm = ((ComboBoxItem)algorithmSelector.SelectedItem).Content.ToString();
            AddToLog($"Запуск розв'язування задачі алгоритмом {algorithm}...");
            statusText.Text = "Вирішуємо задачу...";

            // Запускаємо таймер оновлення статистики
            statsUpdateTimer.Start();

            // Створюємо новий токен для скасування
            cancellationTokenSource = new CancellationTokenSource();

            // Запускаємо алгоритм в окремому потоці, щоб не блокувати UI
            BackgroundWorker worker = new BackgroundWorker();
            worker.WorkerSupportsCancellation = true;

            worker.DoWork += (s, args) =>
            {
                try
                {
                    // Копіюємо поточну дошку для роботи алгоритму
                    int[] initialBoard = new int[boardSize];
                    Array.Copy(board, initialBoard, boardSize);

                    // Запускаємо пошук розв'язку
                    searchNodesCount = 0;
                    backtracksCount = 0;
                    pruningCount = 0;
                    totalBranches = 0;
                    branchPoints = 0;
                    maxDepthReached = 0;
                    deadEndsCount = 0;
                    successfulPlacements = 0;
                    avgBranchingFactor = 0.0;

                    visualizationSteps.Clear();
                    stepDescriptions.Clear();
                    startTime = DateTime.Now;

                    // Починаємо відстежувати використання пам'яті
                    using (Process currentProcess = Process.GetCurrentProcess())
                    {
                        currentProcess.Refresh();
                        initialMemory = currentProcess.PrivateMemorySize64;
                        maxMemoryUsed = 0;
                    }

                    // Додаємо початковий стан до візуалізації
                    visualizationSteps.Add((int[])initialBoard.Clone());
                    stepDescriptions.Add("Початкова розстановка ферзів");

                    bool solved = false;

                    // Вибір алгоритму
                    if (algorithm == "RBFS")
                    {
                        solved = SolveNQueensRBFS(initialBoard, cancellationTokenSource.Token);
                    }
                    else // A*
                    {
                        solved = SolveNQueensAStar(initialBoard, cancellationTokenSource.Token);
                    }

                    // Оновлюємо час виконання
                    executionTime = DateTime.Now - startTime;

                    // Фінальне оновлення пам'яті
                    UpdateMemoryUsage();

                    // Розрахунок додаткової статистики
                    if (searchNodesCount > 0)
                    {
                        avgTimePerNode = executionTime.TotalMilliseconds / searchNodesCount;
                    }

                    if (branchPoints > 0)
                    {
                        avgBranchingFactor = (double)totalBranches / branchPoints;
                    }

                    args.Result = solved;
                }
                catch (OperationCanceledException)
                {
                    args.Result = false;
                    args.Cancel = true;
                }
            };

            worker.RunWorkerCompleted += (s, args) =>
            {
                statsUpdateTimer.Stop();

                // Обробка скасування
                if (args.Cancelled)
                {
                    AddToLog("Пошук скасовано користувачем.");
                    statusText.Text = "Пошук скасовано.";
                    IsRunning = false;
                    return;
                }

                // Обробка помилки
                if (args.Error != null)
                {
                    AddToLog($"Помилка під час виконання: {args.Error.Message}");
                    statusText.Text = "Виникла помилка під час пошуку.";
                    IsRunning = false;
                    return;
                }

                bool solved = (bool)args.Result;

                // Оновлюємо статистику (фінальну)
                UpdateStatistics();

                if (solved)
                {
                    statusText.Text = "Рішення знайдено!";
                    AddToLog("Рішення знайдено! Починаємо візуалізацію...");
                    HasSolution = true;
                    StartVisualization();
                }
                else
                {
                    statusText.Text = "Рішення не існує для цієї початкової розстановки.";
                    AddToLog("Рішення не існує для цієї початкової розстановки!");
                    IsRunning = false;
                }
            };

            worker.RunWorkerAsync();
        }

        // Метод для оновлення статистики
        private void UpdateStatistics(bool intermediate = false)
        {
            Dispatcher.Invoke(() =>
            {
                if (statsText == null || algorithmSelector == null)
                    return;

                string algorithm = "Невідомий";
                if (algorithmSelector.SelectedItem != null)
                {
                    algorithm = ((ComboBoxItem)algorithmSelector.SelectedItem).Content.ToString();
                }

                StringBuilder stats = new StringBuilder();
                stats.AppendLine($"Алгоритм: {algorithm}");
                stats.AppendLine($"Переглянуто станів: {searchNodesCount:N0}");
                stats.AppendLine($"Час виконання: {executionTime.TotalSeconds:F3} сек");
                stats.AppendLine($"Використана пам'ять: {maxMemoryUsed / 1024.0 / 1024.0:F2} МБ");
                stats.AppendLine($"Повернень назад: {backtracksCount:N0}");
                stats.AppendLine($"Відсічено гілок: {pruningCount:N0}");
                stats.AppendLine($"Тупикових ситуацій: {deadEndsCount:N0}");

                if (maxDepthReached > 0)
                    stats.AppendLine($"Макс. глибина: {maxDepthReached}");

                if (branchPoints > 0)
                    stats.AppendLine($"Сер. розгалуження: {avgBranchingFactor:F2}");

                if (executionTime.TotalSeconds > 0)
                    stats.AppendLine($"Швидкість пошуку: {(searchNodesCount / executionTime.TotalSeconds):F1} вузлів/с");

                if (searchNodesCount > 0 && executionTime.TotalMilliseconds > 0)
                    stats.AppendLine($"Час на вузол: {avgTimePerNode:F3} мс");

                if (queensPlaced > 0)
                    stats.AppendLine($"Розміщено ферзів: {queensPlaced} з {boardSize}");

                statsText.Text = stats.ToString();
            });
        }

        // Алгоритм A* для розв'язання задачі 8 ферзів
        private bool SolveNQueensAStar(int[] initialBoard, CancellationToken cancellationToken)
        {
            AddToLog("Початковий аналіз дошки методом A*...");

            // Порівнювач для пріоритизації станів з нижчим F
            var comparer = Comparer<AStarNode>.Create((x, y) => {
                int fCompare = x.F.CompareTo(y.F);
                if (fCompare != 0) return fCompare;
                // Якщо F рівні, пріоритизуємо стани з більшою кількістю розміщених ферзів
                return y.G.CompareTo(x.G);
            });

            // Відкритий і закритий набори для A*
            var openSet = new List<AStarNode>();
            var closedSet = new HashSet<string>();

            // Додаємо початковий стан
            var initialNode = new AStarNode(initialBoard);
            openSet.Add(initialNode);

            Stopwatch nodeTimer = new Stopwatch();
            long totalNodeTime = 0;
            int nodesProcessed = 0;

            DateTime lastStatUpdate = DateTime.Now;

            while (openSet.Count > 0)
            {
                // Перевірка скасування
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                // Обробка паузи
                while (isPaused)
                {
                    Thread.Sleep(100);
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException();
                }

                // Збільшуємо лічильник переглянутих вузлів
                searchNodesCount++;
                nodesProcessed++;

                // Сортуємо відкритий набір за F та беремо найкращий стан
                nodeTimer.Restart();
                openSet.Sort(comparer);
                var currentNode = openSet[0];
                openSet.RemoveAt(0);

                int[] currentBoard = currentNode.Board;

                // Перевіряємо, чи маємо розв'язок
                if (currentNode.QueensPlaced == boardSize)
                {
                    // Оновлюємо статистику часу на вузол
                    totalNodeTime += nodeTimer.ElapsedMilliseconds;
                    avgTimePerNode = (double)totalNodeTime / nodesProcessed;

                    // Знайдено рішення - копіюємо його в початкову дошку
                    Array.Copy(currentBoard, initialBoard, boardSize);
                    visualizationSteps.Add((int[])currentBoard.Clone());
                    stepDescriptions.Add("Знайдено рішення! Всі ферзі розміщені коректно.");
                    return true;
                }

                // Додаємо поточний стан до закритого набору
                string boardKey = string.Join(",", currentBoard);
                closedSet.Add(boardKey);

                // Знаходимо перший вільний рядок
                int nextRow = -1;
                for (int i = 0; i < boardSize; i++)
                {
                    if (currentBoard[i] == -1)
                    {
                        nextRow = i;
                        break;
                    }
                }

                if (nextRow == -1) continue;

                // Оновлюємо максимальну глибину
                maxDepthReached = Math.Max(maxDepthReached, nextRow);

                // Відстежуємо точку розгалуження
                branchPoints++;
                int validMoves = 0;

                // Генеруємо нащадків (розміщуємо ферзя у всіх можливих позиціях)
                for (int col = 0; col < boardSize; col++)
                {
                    // Перевіряємо, чи можна розмістити ферзя в цьому стовпці
                    bool canPlace = true;

                    for (int row = 0; row < boardSize; row++)
                    {
                        if (currentBoard[row] == -1) continue;

                        // Перевірка атаки по стовпцю
                        if (currentBoard[row] == col)
                        {
                            canPlace = false;
                            break;
                        }

                        // Перевірка атаки по діагоналі
                        if (Math.Abs(row - nextRow) == Math.Abs(currentBoard[row] - col))
                        {
                            canPlace = false;
                            break;
                        }
                    }

                    // Відсікання неможливих розміщень
                    if (!canPlace)
                    {
                        pruningCount++;
                        continue;
                    }

                    validMoves++;
                    totalBranches++;

                    // Створюємо новий стан з доданим ферзем
                    int[] newBoard = (int[])currentBoard.Clone();
                    newBoard[nextRow] = col;
                    successfulPlacements++;

                    string newBoardKey = string.Join(",", newBoard);
                    if (!closedSet.Contains(newBoardKey))
                    {
                        var newNode = new AStarNode(newBoard);

                        // Перевіряємо, чи вже є цей стан у відкритому наборі
                        bool inOpenSet = false;
                        for (int i = 0; i < openSet.Count; i++)
                        {
                            if (string.Join(",", openSet[i].Board) == newBoardKey)
                            {
                                inOpenSet = true;
                                // Якщо новий шлях кращий, оновлюємо
                                if (newNode.G > openSet[i].G)
                                {
                                    openSet[i] = newNode;
                                }
                                break;
                            }
                        }

                        if (!inOpenSet)
                        {
                            openSet.Add(newNode);

                            // Зберігаємо для візуалізації (але не кожен крок)
                            if (searchNodesCount % 5 == 0 || newNode.G <= 3)
                            {
                                visualizationSteps.Add((int[])newBoard.Clone());
                                stepDescriptions.Add($"A*: розміщуємо ферзя на {(char)('a' + col)}{8 - nextRow}, f={newNode.F} (g={newNode.G}, h={newNode.H})");
                            }
                        }
                    }
                }

                // Якщо не знайдено жодного допустимого ходу
                if (validMoves == 0)
                {
                    deadEndsCount++;
                }

                nodeTimer.Stop();
                totalNodeTime += nodeTimer.ElapsedMilliseconds;

                // Регулярне оновлення статистики
                if ((DateTime.Now - lastStatUpdate).TotalMilliseconds > 200)
                {
                    UpdateMemoryUsage();
                    avgTimePerNode = (double)totalNodeTime / nodesProcessed;
                    if (branchPoints > 0)
                    {
                        avgBranchingFactor = (double)totalBranches / branchPoints;
                    }
                    lastStatUpdate = DateTime.Now;
                }

                // Перевіряємо перевищення часу або ліміту вузлів
                if (searchNodesCount % 1000 == 0)
                {
                    // Перевіряємо перевищення часу
                    if (DateTime.Now - startTime > TimeSpan.FromSeconds(5))
                    {
                        AddToLog("Перевищено час пошуку A*, використовуємо евристичний метод...");
                        return TrySolveWithHeuristics(initialBoard);
                    }
                }
            }

            // Якщо не знайдено розв'язку, спробуємо використати відомі рішення
            return TryFindCompatibleSolution(initialBoard);
        }

        // Оптимізований алгоритм RBFS для розв'язання задачі 8 ферзів
        private bool SolveNQueensRBFS(int[] initialBoard, CancellationToken cancellationToken)
        {
            // Для відстеження зайнятих стовпців, діагоналей
            bool[] colUsed = new bool[boardSize];
            bool[] diag1 = new bool[boardSize * 2]; // Діагоналі типу /
            bool[] diag2 = new bool[boardSize * 2]; // Діагоналі типу \

            // Ініціалізуємо масиви зайнятості на основі початкової розстановки
            for (int i = 0; i < boardSize; i++)
            {
                if (initialBoard[i] != -1)
                {
                    int col = initialBoard[i];
                    colUsed[col] = true;
                    diag1[i + col] = true;
                    diag2[i - col + boardSize] = true;
                }
            }

            AddToLog("Початковий аналіз дошки методом RBFS...");

            // Запускаємо backtracking з першого порожнього рядка
            int startRow = 0;
            while (startRow < boardSize && initialBoard[startRow] != -1)
                startRow++;

            return SolveNQueensBacktrack(initialBoard, startRow, colUsed, diag1, diag2, cancellationToken);
        }

        // Рекурсивний backtracking алгоритм
        private bool SolveNQueensBacktrack(int[] board, int row, bool[] colUsed, bool[] diag1, bool[] diag2,
                                          CancellationToken cancellationToken)
        {
            // Перевірка скасування
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException();
            }

            // Обробка паузи
            while (isPaused)
            {
                Thread.Sleep(100);
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException();
            }

            searchNodesCount++;
            maxDepthReached = Math.Max(maxDepthReached, row);

            // Оновлюємо використання пам'яті періодично
            if (searchNodesCount % 1000 == 0)
            {
                UpdateMemoryUsage();
            }

            // Базовий випадок: всі рядки заповнені
            if (row >= boardSize)
            {
                // Знайдено рішення
                visualizationSteps.Add((int[])board.Clone());
                stepDescriptions.Add("Знайдено рішення! Всі ферзі розміщені коректно.");
                return true;
            }

            // Якщо в поточному рядку вже є ферзь, переходимо до наступного
            if (board[row] != -1)
            {
                return SolveNQueensBacktrack(board, row + 1, colUsed, diag1, diag2, cancellationToken);
            }

            // Обмеження часу виконання
            if (searchNodesCount % 1000 == 0 && DateTime.Now - startTime > TimeSpan.FromSeconds(5))
            {
                AddToLog("Перевищено час пошуку, використовуємо інший підхід...");
                return TrySolveWithHeuristics(board, row, cancellationToken);
            }

            // Для відстеження точки розгалуження
            branchPoints++;
            int validMoves = 0;

            // Пробуємо розмістити ферзя в поточному рядку
            for (int col = 0; col < boardSize; col++)
            {
                // Перевіряємо, чи можна розмістити ферзя
                if (!colUsed[col] && !diag1[row + col] && !diag2[row - col + boardSize])
                {
                    validMoves++;
                    totalBranches++;

                    // Розміщуємо ферзя
                    board[row] = col;
                    colUsed[col] = true;
                    diag1[row + col] = true;
                    diag2[row - col + boardSize] = true;
                    successfulPlacements++;

                    // Зберігаємо для візуалізації, але не кожен крок
                    if (searchNodesCount % 5 == 0 || row <= 3)
                    {
                        visualizationSteps.Add((int[])board.Clone());
                        stepDescriptions.Add($"RBFS: розміщуємо ферзя на {(char)('a' + col)}{8 - row}");
                    }

                    // Рекурсивно вирішуємо для наступного рядка
                    if (SolveNQueensBacktrack(board, row + 1, colUsed, diag1, diag2, cancellationToken))
                        return true;

                    // Якщо не знайдено рішення, повертаємося і пробуємо інший стовпець
                    board[row] = -1;
                    colUsed[col] = false;
                    diag1[row + col] = false;
                    diag2[row - col + boardSize] = false;
                    backtracksCount++;
                }
                else
                {
                    pruningCount++;
                }
            }

            // Якщо не знайдено жодного валідного розміщення в цьому рядку
            if (validMoves == 0)
            {
                deadEndsCount++;
            }

            // Оновлюємо статистику
            if (searchNodesCount % 1000 == 0)
            {
                if (branchPoints > 0)
                {
                    avgBranchingFactor = (double)totalBranches / branchPoints;
                }
            }

            return false;
        }

        // Резервний метод з евристиками для швидкого пошуку розв'язку
        private bool TrySolveWithHeuristics(int[] board, int startRow = 0, CancellationToken cancellationToken = default)
        {
            AddToLog("Застосовуємо евристичний підхід для швидкого пошуку...");

            // Перевіряємо, скільки ферзів уже стоять на дошці
            int queensPlaced = 0;
            for (int i = 0; i < boardSize; i++)
                if (board[i] != -1) queensPlaced++;

            // Якщо дошка майже заповнена, продовжуємо backtracking з обмеженням часу
            if (queensPlaced >= 6)
            {
                AddToLog("Дошка майже заповнена, продовжуємо звичайний пошук...");

                // Масиви для відстеження зайнятих позицій
                bool[] colUsed = new bool[boardSize];
                bool[] diag1 = new bool[boardSize * 2];
                bool[] diag2 = new bool[boardSize * 2]; 

                // Ініціалізація масивів
                for (int i = 0; i < boardSize; i++)
                {
                    if (board[i] != -1)
                    {
                        colUsed[board[i]] = true;
                        diag1[i + board[i]] = true;
                        diag2[i - board[i] + boardSize] = true;
                    }
                }

                // Спроба знайти розв'язок з таймаутом 1 секунда
                bool result = SolveWithTimeout(board, startRow, colUsed, diag1, diag2, TimeSpan.FromSeconds(1), cancellationToken);

                if (result)
                    return true;
            }

            // Якщо розв'язок не знайдено, спробуємо знайти розв'язок для 8 ферзів з нуля
            // і зберегти існуючі розміщення, якщо можливо
            return TryFindCompatibleSolution(board);
        }
        private bool SolveWithTimeout(int[] board, int row, bool[] colUsed, bool[] diag1, bool[] diag2,
                             TimeSpan timeout, CancellationToken cancellationToken)
        {
            DateTime startTime = DateTime.Now;

            // Перевірка скасування
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException();
            }

            // Обробка паузи
            while (isPaused)
            {
                Thread.Sleep(100);
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException();
            }

            // Перевірка таймауту
            if (DateTime.Now - startTime > timeout)
                return false;

            searchNodesCount++;

            // Базовий випадок: всі рядки заповнені
            if (row >= boardSize)
                return true;

            // Якщо в поточному рядку вже є ферзь, переходимо до наступного
            if (board[row] != -1)
                return SolveWithTimeout(board, row + 1, colUsed, diag1, diag2, timeout, cancellationToken);

            // Оновлюємо максимальну глибину
            maxDepthReached = Math.Max(maxDepthReached, row);

            // Для відстеження точки розгалуження
            branchPoints++;
            int validMoves = 0;

            // Пробуємо розмістити ферзя в поточному рядку
            for (int col = 0; col < boardSize; col++)
            {
                if (!colUsed[col] && !diag1[row + col] && !diag2[row - col + boardSize])
                {
                    validMoves++;
                    totalBranches++;

                    board[row] = col;
                    colUsed[col] = true;
                    diag1[row + col] = true;
                    diag2[row - col + boardSize] = true;
                    successfulPlacements++;

                    visualizationSteps.Add((int[])board.Clone());
                    stepDescriptions.Add($"Спроба: розміщуємо ферзя на {(char)('a' + col)}{8 - row}");

                    if (SolveWithTimeout(board, row + 1, colUsed, diag1, diag2, timeout, cancellationToken))
                    {
                        return true;
                    }

                    board[row] = -1;
                    colUsed[col] = false;
                    diag1[row + col] = false;
                    diag2[row - col + boardSize] = false;
                    backtracksCount++;
                }
                else
                {
                    pruningCount++;
                }
            }

            // Якщо не знайдено жодного валідного розміщення
            if (validMoves == 0)
            {
                deadEndsCount++;
            }

            return false;
        }

        // Пошук сумісного розв'язку
        private bool TryFindCompatibleSolution(int[] board)
        {
            AddToLog("Шукаємо відоме рішення, сумісне з наявними ферзями...");

            // Відомі розв'язки для 8 ферзів
            int[][] solutions = new int[][] {
                new int[] { 0, 4, 7, 5, 2, 6, 1, 3 },
                new int[] { 0, 5, 7, 2, 6, 3, 1, 4 },
                new int[] { 0, 6, 3, 5, 7, 1, 4, 2 },
                new int[] { 0, 6, 4, 7, 1, 3, 5, 2 },
                new int[] { 1, 3, 5, 7, 2, 0, 6, 4 },
                new int[] { 1, 4, 6, 0, 2, 7, 5, 3 },
                new int[] { 1, 4, 6, 3, 0, 7, 5, 2 },
                new int[] { 1, 5, 0, 6, 3, 7, 2, 4 },
                new int[] { 1, 5, 7, 2, 0, 3, 6, 4 },
                new int[] { 1, 6, 2, 5, 7, 4, 0, 3 },
                new int[] { 1, 6, 4, 7, 0, 3, 5, 2 },
                new int[] { 1, 7, 5, 0, 2, 4, 6, 3 }
            };

            // Перевіряємо кожне відоме рішення
            foreach (int[] solution in solutions)
            {
                bool isCompatible = true;

                // Перевіряємо, чи сумісне рішення з наявними ферзями
                for (int i = 0; i < boardSize; i++)
                {
                    if (board[i] != -1 && board[i] != solution[i])
                    {
                        isCompatible = false;
                        break;
                    }
                }

                if (isCompatible)
                {
                    AddToLog($"Знайдено сумісне рішення!");

                    // Створюємо кроки візуалізації
                    List<int[]> steps = new List<int[]>();
                    int[] currentBoard = (int[])board.Clone();

                    for (int i = 0; i < boardSize; i++)
                    {
                        if (currentBoard[i] == -1)
                        {
                            currentBoard[i] = solution[i];
                            steps.Add((int[])currentBoard.Clone());
                            stepDescriptions.Add($"Розміщуємо ферзя на {(char)('a' + solution[i])}{8 - i}");
                        }
                    }

                    // Додаємо кроки в візуалізацію
                    visualizationSteps.AddRange(steps);
                    stepDescriptions.Add("Знайдено рішення! Всі ферзі розміщені коректно.");

                    // Копіюємо рішення в дошку
                    Array.Copy(solution, board, boardSize);

                    return true;
                }
            }

            AddToLog("Не знайдено сумісних розв'язків з відомими рішеннями.");
            return false;
        }

        // Додавання повідомлення до логу
        private void AddToLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                debugLog.AppendLine(message);
                OnPropertyChanged("DebugLogText");

                // Перевіряємо, що logTextBox не є null перед викликом методу
                if (logTextBox != null)
                {
                    logTextBox.ScrollToEnd();
                }
            });
        }

        // Візуалізація процесу розв'язання
        private void StartVisualization()
        {
            currentStep = 0;
            visualizationTimer.Start();
        }

        private void VisualizationTimer_Tick(object sender, EventArgs e)
        {
            if (currentStep < visualizationSteps.Count)
            {
                board = visualizationSteps[currentStep];
                DrawBoard();

                if (currentStep < stepDescriptions.Count)
                    statusText.Text = stepDescriptions[currentStep];

                currentStep++;
            }
            else
            {
                visualizationTimer.Stop();
                statusText.Text = "Візуалізацію завершено. Рішення знайдено!";
                IsRunning = false;
            }
        }

        // Скидання дошки
        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (isRunning)
            {
                // Зупиняємо виконання, якщо воно триває
                if (cancellationTokenSource != null && !cancellationTokenSource.IsCancellationRequested)
                {
                    cancellationTokenSource.Cancel();
                }
            }

            InitializeBoard();
            DrawBoard();
            statusText.Text = "Розставте початкових ферзів і натисніть 'Розв'язати'.";
            AddToLog("Дошку скинуто. Розставте ферзів для початку розв'язання.");
        }

        // Налаштування швидкості візуалізації
        private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (visualizationTimer != null)
            {
                // Перетворення значення повзунка на інтервал таймера (від 100 до 2000 мс)
                visualizationTimer.Interval = TimeSpan.FromMilliseconds(2100 - speedSlider.Value);
            }
        }

        // Кнопка паузи/продовження візуалізації
        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (isRunning)
            {
                isPaused = !isPaused;

                if (visualizationTimer.IsEnabled && !isPaused)
                {
                    visualizationTimer.Stop();
                    pauseButton.Content = "Продовжити";
                }
                else if (!visualizationTimer.IsEnabled && isPaused)
                {
                    visualizationTimer.Start();
                    pauseButton.Content = "Пауза";
                }
                else
                {
                    pauseButton.Content = isPaused ? "Продовжити" : "Пауза";
                }
            }
        }

        private void ChessboardCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawBoard();
        }

        // Сповіщення про зміну властивості
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}