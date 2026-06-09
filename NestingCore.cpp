using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using netDxf;
using netDxf.Entities;

namespace NestingApp
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Point2D { public double X; public double Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct PlacementResult { public int PartID; public double OffsetX; public double OffsetY; public double RotationAngle; public int SheetNumber; }

    class Program
    {
        [DllImport("NestingEngine.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void RunNesting(double l, double w, double m, double s, int c, int[] vc, IntPtr[] pd, [Out] PlacementResult[] r);

        static void Main(string[] args)
        {
            Console.WriteLine("=== СИСТЕМА РАСКРОЯ ЧПУ ===");
            double sheetLength = 3000.0; double sheetWidth = 1500.0; double margin = 5.0; double spacing = 1.0;
            string inputFile = "part1.dxf"; 
            
            DxfDocument doc;
            try { doc = DxfDocument.Load(inputFile); }
            catch {
                Console.WriteLine($"Ошибка: Положите файл '{inputFile}' в папку с программой!");
                return;
            }

            var polylines = doc.LwPolylines.ToList();
            int partCount = polylines.Count;
            int[] vertexCounts = new int[partCount];
            IntPtr[] partsDataPointers = new IntPtr[partCount];
            List<Point2D[]> partsPointsList = new List<Point2D[]>();

            for (int i = 0; i < partCount; i++)
            {
                var vertices = polylines[i].Vertexes;
                vertexCounts[i] = vertices.Count;
                Point2D[] pointsArray = new Point2D[vertices.Count];
                for (int j = 0; j < vertices.Count; j++)
                    pointsArray[j] = new Point2D { X = vertices[j].Position.X, Y = vertices[j].Position.Y };
                
                partsPointsList.Add(pointsArray);
                int size = Marshal.SizeOf(typeof(Point2D)) * pointsArray.Length;
                IntPtr ptr = Marshal.AllocHGlobal(size);
                for (int j = 0; j < pointsArray.Length; j++)
                    Marshal.StructureToPtr(pointsArray[j], ptr + (j * Marshal.SizeOf(typeof(Point2D))), false);
                partsDataPointers[i] = ptr;
            }

            PlacementResult[] results = new PlacementResult[partCount];
            Console.WriteLine("Расчет оптимальной укладки на листы...");
            RunNesting(sheetLength, sheetWidth, margin, spacing, partCount, vertexCounts, partsDataPointers, results);

            foreach (var ptr in partsDataPointers) Marshal.FreeHGlobal(ptr);

            DxfDocument outDoc = new DxfDocument();
            outDoc.Entities.Add(new LwPolyline(new[] { new LwPolylineVertex(0, 0), new LwPolylineVertex(sheetLength, 0), new LwPolylineVertex(sheetLength, sheetWidth), new LwPolylineVertex(0, sheetWidth) }, true));

            for (int i = 0; i < partCount; i++)
            {
                var res = results[i];
                if (res.SheetNumber == 1) {
                    var clonedPoly = (LwPolyline)polylines[res.PartID].Clone();
                    Vector3 translation = new Vector3(res.OffsetX, res.OffsetY, 0);
                    clonedPoly.TransformBy(Matrix3.Identity, translation);
                    
                    if (res.RotationAngle != 0)
                    {
                        clonedPoly.TransformBy(Matrix3.RotationZ(res.RotationAngle * Math.PI / 180.0), Vector3.Zero);
                    }
                    outDoc.Entities.Add(clonedPoly);
                }
            }

            string outputFile = "ResultSheet_1.dxf";
            outDoc.Save(outputFile);
            Console.WriteLine($"[УСПЕХ] Карта раскроя сохранена в файл: {outputFile}");
        }
    }
}
