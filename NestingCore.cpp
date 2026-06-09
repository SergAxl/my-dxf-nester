#include <iostream>

extern "C" {
    struct PlacementResult {
        int PartID;
        double OffsetX;
        double OffsetY;
        double RotationAngle;
        int SheetNumber;
    };

    __declspec(dllexport) void RunNesting(
        double sheetLength,
        double sheetWidth,
        double margin,
        double spacing,
        int partCount,
        int* vertexCounts,
        double** partsData,
        PlacementResult* results
    ) {
        // Базовая сетка-заглушка 120x120 для проверки связки C++ и C#
        double currentX = margin;
        double currentY = margin;
        double step = 120.0 + spacing;

        for (int i = 0; i < partCount; ++i) {
            results[i].PartID = i;
            results[i].OffsetX = currentX;
            results[i].OffsetY = currentY;
            results[i].RotationAngle = 0.0;
            results[i].SheetNumber = 1;

            currentX += step;
            if (currentX + 120.0 > sheetLength) {
                currentX = margin;
                currentY += step;
            }
        }
    }
}
