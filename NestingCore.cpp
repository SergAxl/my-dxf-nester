#include <vector>
#include <iostream>

struct Point2D { double X; double Y; };
struct PlacementResult { int PartID; double OffsetX; double OffsetY; double RotationAngle; int SheetNumber; };

extern "C" {
    __declspec(dllexport) void RunNesting(
        double sheetLength, double sheetWidth, double margin, double spacing,
        int partCount, int* vertexCounts, Point2D** partsData, PlacementResult* outResults
    ) {
        double workAreaL = sheetLength - (margin * 2);
        double workAreaW = sheetWidth - (margin * 2);
        double currentX = margin; double currentY = margin; int currentSheet = 1;

        for (int i = 0; i < partCount; ++i) {
            outResults[i].PartID = i;
            outResults[i].OffsetX = currentX;
            outResults[i].OffsetY = currentY;
            outResults[i].RotationAngle = 0.0;
            outResults[i].SheetNumber = currentSheet;

            currentX += 120.0 + spacing; 
            if (currentX > workAreaL) {
                currentX = margin;
                currentY += 120.0 + spacing;
                if (currentY > workAreaW) {
                    currentY = margin;
                    currentSheet++;
                }
            }
        }
    }
}
