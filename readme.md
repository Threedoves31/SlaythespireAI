# 视觉方案
## 1. 安装依赖
conda activate sts
pip install -r src/visual_ai/requirements.txt

## 2. 测试屏幕捕获
python -m src.visual_ai.vision.screen_capture

## 3. 运行 AI（调试模式）
python src/visual_ai/main.py --debug

# 底层方案
将mod文件放入mods文件夹即可