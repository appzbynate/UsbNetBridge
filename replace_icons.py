import os
from PIL import Image

src_img_path = r'C:\Users\Nate\.gemini\antigravity\brain\f2f8499e-d563-4c28-8e06-674728b56db8\icon_full_bleed.png'
base_dir = r'C:\Users\Nate\Projects\UsbNetBridge'

if not os.path.exists(src_img_path):
    print("Source image not found!")
    exit(1)

img = Image.open(src_img_path).convert("RGBA")

# Android mipmap sizes
mipmap_sizes = {
    'mipmap-mdpi': 48,
    'mipmap-hdpi': 72,
    'mipmap-xhdpi': 96,
    'mipmap-xxhdpi': 144,
    'mipmap-xxxhdpi': 192
}

android_res_dir = os.path.join(base_dir, 'android', 'app', 'src', 'main', 'res')

for folder, size in mipmap_sizes.items():
    folder_path = os.path.join(android_res_dir, folder)
    if os.path.exists(folder_path):
        resized = img.resize((size, size), Image.Resampling.LANCZOS)
        resized.save(os.path.join(folder_path, 'ic_launcher.png'))
        resized.save(os.path.join(folder_path, 'ic_launcher_round.png'))
        print(f"Updated {folder}")

# Update Windows .ico
win_ico_path = os.path.join(base_dir, 'windows', 'UsbNetBridge.Client', 'app.ico')
img.save(win_ico_path, format='ICO', sizes=[(256, 256), (128, 128), (64, 64), (32, 32), (16, 16)])
print("Updated app.ico")

# If title-logo.png exists, let's update it. Wait, title-logo.png might be a wide logo with text. 
# Let's check its current dimensions first before blindly overwriting.
