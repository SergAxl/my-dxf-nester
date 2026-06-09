using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using netDxf;
using netDxf.Entities;

namespace NestingApp
{
    class Program : Application
    {
        [STAThread]
        public static void Main()
        {
            var app = new Program();
            app.Run(new MainWindow());
        }
    }

    public class MainWindow : Window
    {
        [DllImport("NestingEngine.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void OptimizeNesting(double[] x, double[] y, int count, double sheetW, double sheetH, double margin, double spacing, ref double outX, ref double outY);

        private TextBox txtWidth;
        private TextBox txtHeight;
        private TextBox txtMargin;
        private TextBox txtSpacing;
        private TextBlock lblStatus;
        private Canvas previewCanvas;

        private string? loadedFilePath = null;
        private List<Polyline2D> currentPolylines = new List<Polyline2D>();

        public MainWindow()
        {
            Title = "CNC Nesting System (AutoCAD 2023 & Corel 2021 Ready)";
            Width = 950;
            Height = 650;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));

            Grid mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(280) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            Content = mainGrid;

            StackPanel leftPanel = new StackPanel { Margin = new Thickness(15) };
            Grid.SetColumn(leftPanel, 0);
            mainGrid.Children.Add(leftPanel);

            leftPanel.Children.Add(new TextBlock { Text = "ПАРАМЕТРЫ РАСКРОЯ", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(0,0,0,20) });

            txtWidth = CreateInputGroup(leftPanel, "Длина листа (X), мм:", "1500");
            txtHeight = CreateInputGroup(leftPanel, "Ширина листа (Y), мм:", "1500");
            txtMargin = CreateInputGroup(leftPanel, "Отступ от края, мм:", "10");
            txtSpacing = CreateInputGroup(leftPanel, "Зазор между деталями, мм:", "1");

            Button btnOpen = CreateButton("Загрузить DXF детали", "#2D79E6");
            btnOpen.Click += BtnOpen_Click;
            leftPanel.Children.Add(btnOpen);

            Button btnNest = CreateButton("Рассчитать раскрой", "#27AE60");
            btnNest.Click += BtnNest_Click;
            leftPanel.Children.Add(btnNest);

            lblStatus = new TextBlock { Text = "Ожидание загрузки файла...", Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 20, 0, 0), FontSize = 12 };
            leftPanel.Children.Add(lblStatus);

            Border canvasBorder = new Border { Margin = new Thickness(15), Background = new SolidColorBrush(Color.FromRgb(15, 15, 15)), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = Brushes.DimGray };
            Grid.SetColumn(canvasBorder, 1);
            mainGrid.Children.Add(canvasBorder);

            previewCanvas = new Canvas { ClipToBounds = true };
            canvasBorder.Child = previewCanvas;
        }

        private TextBox CreateInputGroup(StackPanel panel, string labelText, string defaultValue)
        {
            panel.Children.Add(new TextBlock { Text = labelText, Foreground = Brushes.DarkGray, Margin = new Thickness(0, 5, 0, 2) });
            TextBox tb = new TextBox { Text = defaultValue, Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)), Foreground = Brushes.White, BorderBrush = Brushes.Gray, Padding = new Thickness(5), Margin = new Thickness(0, 0, 0, 15) };
            panel.Children.Add(tb);
            return tb;
        }

        private Button CreateButton(string text, string hexColor)
        {
            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            return new Button {
                Content = text, Height = 38, Background = new SolidColorBrush(color), Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold, BorderThickness = new Thickness(0), Margin = new Thickness(0, 5, 0, 5),
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog { Filter = "DXF файлы (*.dxf)|*.dxf" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    loadedFilePath = dlg.FileName;
                    DxfDocument doc = DxfDocument.Load(loadedFilePath);
                    
                    if (doc == null) {
                        MessageBox.Show("Ошибка разбора структуры файла.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    currentPolylines.Clear();

                    // 1. Сбор готовых полилиний
                    if (doc.Entities.Polylines2D != null) currentPolylines.AddRange(doc.Entities.Polylines2D);
                    if (doc.Entities.LwPolylines != null)
                    {
                        foreach (var lw in doc.Entities.LwPolylines)
                        {
                            var v = lw.Vertexes.Select(pt => new Polyline2DVertex(pt.Position.X, pt.Position.Y)).ToList();
                            currentPolylines.Add(new Polyline2D(v) { IsClosed = lw.IsClosed });
                        }
                    }

                    // 2. АВТОМАТИЧЕСКАЯ СШИВКА ОТРЕЗКОВ (LINE) В ПОЛИЛИНИИ
                    if (doc.Entities.Lines != null && doc.Entities.Lines.Count > 0)
                    {
                        var segments = doc.Entities.Lines.Select(l => new Tuple<Vector2, Vector2>(
                            new Vector2(l.StartPoint.X, l.StartPoint.Y), 
                            new Vector2(l.EndPoint.X, l.EndPoint.Y))).ToList();

                        var stitchedPolys = StitchSegmentsIntoPolylines(segments);
                        currentPolylines.AddRange(stitchedPolys);
                    }

                    lblStatus.Text = $"Файл: {System.IO.Path.GetFileName(loadedFilePath)}\nНайдено деталей: {currentPolylines.Count}";
                    
                    if (currentPolylines.Count == 0) {
                        MessageBox.Show("Не удалось собрать контуры. Убедитесь, что деталь не содержит разрывов.", "Пустой чертеж", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    DrawPreview();
                }
                catch (Exception ex) {
                    MessageBox.Show($"Ошибка чтения файла: {ex.Message}", "Ошибка DXF", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Логика автоматической склейки векторов Corel / AutoCAD
        private List<Polyline2D> StitchSegmentsIntoPolylines(List<Tuple<Vector2, Vector2>> segments)
        {
            var polylines = new List<Polyline2D>();
            double epsilon = 0.01; // Допуск на разрыв стыков в мм

            while (segments.Count > 0)
            {
                var currentChain = new List<Vector2>();
                var first = segments[0];
                segments.RemoveAt(0);

                currentChain.Add(first.Item1);
                currentChain.Add(first.Item2);

                bool added;
                do
                {
                    added = false;
                    for (int i = 0; i < segments.Count; i++)
                    {
                        var seg = segments[i];
                        // Проверка стыковки с концом цепи
                        if (Vector2.Distance(currentChain.Last(), seg.Item1) < epsilon) {
                            currentChain.Add(seg.Item2); segments.RemoveAt(i); added = true; break;
                        }
                        if (Vector2.Distance(currentChain.Last(), seg.Item2) < epsilon) {
                            currentChain.Add(seg.Item1); segments.RemoveAt(i); added = true; break;
                        }
                        // Проверка стыковки с началом цепи
                        if (Vector2.Distance(currentChain.First(), seg.Item1) < epsilon) {
                            currentChain.Insert(0, seg.Item2); segments.RemoveAt(i); added = true; break;
                        }
                        if (Vector2.Distance(currentChain.First(), seg.Item2) < epsilon) {
                            currentChain.Insert(0, seg.Item1); segments.RemoveAt(i); added = true; break;
                        }
                    }
                } while (added);

                if (currentChain.Count >= 3)
                {
                    var vertices = currentChain.Select(v => new Polyline2DVertex(v.X, v.Y)).ToList();
                    bool isClosed = Vector2.Distance(currentChain.First(), currentChain.Last()) < epsilon;
                    polylines.Add(new Polyline2D(vertices) { IsClosed = isClosed });
                }
            }
            return polylines;
        }

        private void BtnNest_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(loadedFilePath) || currentPolylines.Count == 0) {
                MessageBox.Show("Сначала выберите файл через кнопку загрузки!", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!double.TryParse(txtWidth.Text, out double sW) || !double.TryParse(txtHeight.Text, out double sH) ||
                !double.TryParse(txtMargin.Text, out double margin) || !double.TryParse(txtSpacing.Text, out double spacing)) {
                MessageBox.Show("Проверьте числовые параметры!", "Ошибка ввода", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var poly = currentPolylines.FirstOrDefault();
                if (poly == null || poly.Vertexes.Count < 3) return;

                int vCount = poly.Vertexes.Count;
                double[] xCoords = poly.Vertexes.Select(v => v.Position.X).ToArray();
                double[] yCoords = poly.Vertexes.Select(v => v.Position.Y).ToArray();

                double outX = 0, outY = 0;
                OptimizeNesting(xCoords, yCoords, vCount, sW, sH, margin, spacing, ref outX, ref outY);

                DxfDocument resultDoc = new DxfDocument();
                Polyline2D sheetPoly = new Polyline2D(new[] {
                    new Polyline2DVertex(0, 0), new Polyline2DVertex(sW, 0),
                    new Polyline2DVertex(sW, sH), new Polyline2DVertex(0, sH)
                }) { IsClosed = true };
                resultDoc.Entities.Add(sheetPoly);

                var nestedVertices = poly.Vertexes.Select(v => new Polyline2DVertex(v.Position.X + outX, v.Position.Y + outY)).ToList();
                Polyline2D nestedPoly = new Polyline2D(nestedVertices) { IsClosed = true };
                resultDoc.Entities.Add(nestedPoly);

                string outPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(loadedFilePath)!, "Result_Layout.dxf");
                resultDoc.Save(outPath);

                lblStatus.Text = $"Раскрой выполнен успешно!\nСохранено в: {System.IO.Path.GetFileName(outPath)}";
                
                DrawResultPreview(sW, sH, nestedVertices);
                MessageBox.Show($"Файл раскроя успешно сгенерирован:\n{outPath}", "Успех ЧПУ", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) {
                MessageBox.Show($"Критический сбой математики: {ex.Message}", "Ошибка расчета", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DrawPreview()
        {
            previewCanvas.Children.Clear();
            if (currentPolylines.Count == 0) return;

            // Находим общие габариты всех деталей для правильного масштабирования на холсте
            double minX = currentPolylines.Min(p => p.Vertexes.Min(v => v.Position.X));
            double maxX = currentPolylines.Max(p => p.Vertexes.Max(v => v.Position.X));
            double minY = currentPolylines.Min(p => p.Vertexes.Min(v => v.Position.Y));
            double maxY = currentPolylines.Max(p => p.Vertexes.Max(v => v.Position.Y));
            
            double width = maxX - minX;
            double height = maxY - minY;
            if (width <= 0) width = 1;
            if (height <= 0) height = 1;

            double scale = Math.Min((previewCanvas.ActualWidth - 40) / width, (previewCanvas.ActualHeight - 40) / height);
            if (double.IsNaN(scale) || scale <= 0) scale = 1.0;

            foreach (var poly in currentPolylines)
            {
                System.Windows.Shapes.Polyline visualPoly = new System.Windows.Shapes.Polyline { Stroke = Brushes.Cyan, StrokeThickness = 1.5 };
                foreach (var v in poly.Vertexes)
                {
                    double x = (v.Position.X - minX) * scale + 20;
                    double y = previewCanvas.ActualHeight - ((v.Position.Y - minY) * scale + 20);
                    visualPoly.Points.Add(new System.Windows.Point(x, y));
                }
                // Замыкаем линию на экране, если она замкнута в данных
                if (poly.IsClosed && poly.Vertexes.Count > 0) {
                    var v = poly.Vertexes.First();
                    visualPoly.Points.Add(new System.Windows.Point((v.Position.X - minX) * scale + 20, previewCanvas.ActualHeight - ((v.Position.Y - minY) * scale + 20)));
                }
                previewCanvas.Children.Add(visualPoly);
            }
        }

        private void DrawResultPreview(double sw, double sh, List<Polyline2DVertex> nested)
        {
            previewCanvas.Children.Clear();
            double scale = Math.Min((previewCanvas.ActualWidth - 40) / sw, (previewCanvas.ActualHeight - 40) / sh);
            if (double.IsNaN(scale) || scale <= 0) scale = 0.2;

            Rectangle sheetRect = new Rectangle { Width = sw * scale, Height = sh * scale, Stroke = Brushes.DarkGray, StrokeThickness = 1, Margin = new Thickness(20) };
            previewCanvas.Children.Add(sheetRect);

            System.Windows.Shapes.Polyline partPoly = new System.Windows.Shapes.Polyline { Stroke = Brushes.Lime, StrokeThickness = 1.5, Margin = new Thickness(20) };
            foreach (var v in nested) {
                partPoly.Points.Add(new System.Windows.Point(v.Position.X * scale, (sh * scale) - (v.Position.Y * scale)));
            }
            if (nested.Count > 0) {
                partPoly.Points.Add(new System.Windows.Point(nested.First().Position.X * scale, (sh * scale) - (nested.First().Position.Y * scale)));
            }
            previewCanvas.Children.Add(partPoly);
        }
    }
}
