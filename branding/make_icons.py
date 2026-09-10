from PIL import Image
from pathlib import Path

src = Image.open(r"C:\Users\Nate\Projects\UsbNetBridge\branding\app-icon-source.png").convert("RGBA")
ico_path = Path(r"C:\Users\Nate\Projects\UsbNetBridge\windows\UsbNetBridge.Client\app.ico")
sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
src.save(ico_path, format="ICO", sizes=sizes)
print("wrote", ico_path, ico_path.stat().st_size)

png256 = Path(r"C:\Users\Nate\Projects\UsbNetBridge\windows\UsbNetBridge.Client\app-icon.png")
src.resize((256, 256), Image.Resampling.LANCZOS).save(png256)
print("wrote", png256)
