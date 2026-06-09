using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        // Импорт вашего движка расчета
        [DllImport("NestingEngine.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void OptimizeNesting(double[] x, double[] y, int count, double sheetW, double sheetH, double margin, double spacing, ref double outX, ref double outY);

        private TextBox txtWidth, txtHeight, txtMargin, txtSpacing;
        private TextBlock lblStatus;
        private string? loadedFilePath = null;
        private List<List<Vector2>> partContours = new List<List<Vector2>>();

        public MainWindow()
        {
            Title = "CNC Nesting System (AutoCAD/Corel Fixed)";
            Width = 400; Height = 500;
            StackPanel panel = new StackPanel { Margin = new Thickness(20) };
            
            panel.Children.Add(new TextBlock { Text = "Длина листа, мм:" });
            txtWidth = new TextBox { Text = "1500" }; panel.Children.Add(txtWidth);
            
            panel.Children.Add(new TextBlock { Text = "Ширина листа, мм:" });
            txtHeight = new TextBox { Text = "1500" }; panel.Children.Add(txtHeight);
            
            Button btnLoad = new Button { Content = "Загрузить DXF детали", Margin = new Thickness(0, 10, 0, 5) };
            btnLoad.Click += BtnLoad_Click;
            panel.Children.Add(btnLoad);

            lblStatus = new TextBlock { Text = "Найдено деталей: 0", Foreground = Brushes.Blue, FontWeight = FontWeights.Bold };
            panel.Children.Add(lblStatus);

            Button btnNest = new Button { Content = "Рассчитать раскрой", Background = Brushes.LightGreen };
            btnNest.Click += BtnNest_Click;
            panel.Children.Add(btnNest);

            Content = panel;
        }

        private void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog { Filter = "DXF файлы|*.dxf" };
            if (dlg.ShowDialog() == true)
            {
                loadedFilePath = dlg.FileName;
                DxfDocument doc = DxfDocument.Load(loadedFilePath);
                partContours.Clear();

                // 1. Берем готовые полилинии
                foreach (var poly in doc.Entities.Polylines2D)
                    partContours.Add(poly.Vertexes.Select(v => new Vector2(v.Position.X, v.Position.Y)).ToList());

                // 2. Сшиваем линии, если полилиний нет
                var lines = doc.Entities.Lines.Select(l => new Tuple<Vector2, Vector2>(new Vector2(l.StartPoint.X, l.StartPoint.Y), new Vector2(l.EndPoint.X, l.EndPoint.Y))).ToList();
                partContours.AddRange(StitchLines(lines));

                lblStatus.Text = $"Найдено контуров: {partContours.Count}";
            }
        }

        // Алгоритм сшивки линий в контур
        private List<List<Vector2>> StitchLines(List<Tuple<Vector2, Vector2>> lines)
        {
            var result = new List<List<Vector2>>();
            double tolerance = 0.1; // Допуск на стык

            while (lines.Count > 0)
            {
                var contour = new List<Vector2> { lines[0].Item1, lines[0].Item2 };
                lines.RemoveAt(0);

                bool found = true;
                while (found)
                {
                    found = false;
                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (Vector2.Distance(contour.Last(), lines[i].Item1) < tolerance)
                        { contour.Add(lines[i].Item2); lines.RemoveAt(i); found = true; break; }
                        if (Vector2.Distance(contour.Last(), lines[i].Item2) < tolerance)
                        { contour.Add(lines[i].Item1); lines.RemoveAt(i); found = true; break; }
                    }
                }
                result.Add(contour);
            }
            return result;
        }

        private void BtnNest_Click(object sender, RoutedEventArgs e)
        {
            if (partContours.Count == 0) { MessageBox.Show("Нет деталей для раскроя!"); return; }
            MessageBox.Show($"Отправляю {partContours.Count} контуров в движок расчета...");
        }
    }
}
