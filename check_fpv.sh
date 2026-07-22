#!/bin/bash
echo "=== FPV HUNTER PROJECT INFO ==="
echo ""
echo "1. Поиск проекта:"
echo "-------------------"
PROJECT_PATH=$(find /storage/emulated/0 -type d -name "FPV_Hunter_FULL" 2>/dev/null | head -1)
if [ -n "$PROJECT_PATH" ]; then
    echo "✅ НАЙДЕН: $PROJECT_PATH"
    cd "$PROJECT_PATH"
    echo ""
    echo "2. Содержимое папки:"
    echo "-------------------"
    ls -la
    echo ""
    echo "3. Файлы проекта:"
    echo "-------------------"
    find . -type f -name "*.cs" -o -name "*.csproj" -o -name "*.sln" -o -name "*.exe" | head -20
    echo ""
    echo "4. Содержимое Program.cs:"
    echo "-------------------"
    cat Program.cs 2>/dev/null | head -30
    echo ""
    echo "5. Содержимое .csproj:"
    echo "-------------------"
    cat *.csproj 2>/dev/null | head -20
else
    echo "❌ ПРОЕКТ НЕ НАЙДЕН!"
    echo ""
    echo "6. Поиск в Download:"
    echo "-------------------"
    ls -la /storage/emulated/0/Download/ | grep -i fpv
fi
echo ""
echo "=== КОНЕЦ ==="
