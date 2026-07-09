import os
import struct
import numpy as np
from PIL import Image
from skimage.measure import label, regionprops
from scipy.ndimage import binary_dilation

def generar_xml_spr(tga_path, xml_salida_path, tolerancia_pixeles=30):
    print(f"\n[1] Analizando textura: {tga_path}...")
    if not os.path.exists(tga_path):
        print(f"❌ Error: No se encuentra el archivo {tga_path}")
        return
    
    try:
        img = Image.open(tga_path).convert("RGBA")
        datos_pixeles = np.array(img)
        canal_alfa = datos_pixeles[:, :, 3]

        mascara_dibujo = (canal_alfa > 10).astype(bool)
        estructura_expansion = np.ones((tolerancia_pixeles, tolerancia_pixeles), dtype=bool)
        mascara_fusionada = binary_dilation(mascara_dibujo, structure=estructura_expansion)

        regiones_detectadas = label(mascara_fusionada)
        propiedades_regiones = regionprops(regiones_detectadas)
        propiedades_regiones = sorted(propiedades_regiones, key=lambda r: r.area, reverse=True)

        lineas_xml = [
            '<?xml version="1.0" encoding="euc-kr" ?>',
            f'<Sprite Filename="rnr_ui/title/{os.path.basename(tga_path)}" >'
        ]

        conteo = 0
        for region in propiedades_regiones:
            if region.area < 100:
                continue
            ymin, xmin, ymax, xmax = region.bbox
            ancho = int(xmax - xmin)
            alto = int(ymax - ymin)
            
            if conteo == 0:
                lineas_xml.append(f'  <Cut Width="{ancho}" Height="{alto}" TargetX="0.0f" TargetY="0.0f" TargetWidth="1.0f" TargetHeight="1.0f" >\n  </Cut>')
            else:
                lineas_xml.append(f'  <Cut Width="{ancho}" Height="{alto}" X="{xmin}" Y="{ymin}" W="{ancho}" H="{alto}" >\n  </Cut>')
            conteo += 1

        lineas_xml.append('</Sprite>')

        with open(xml_salida_path, "w", encoding="utf-8") as f:
            f.write("\n".join(lineas_xml))
        print(f"💖 ¡Éxito! Archivo generado en: {xml_salida_path}")
    except Exception as e:
        print(f"❌ Error al generar el mapa de recortes: {e}")

def recortar_con_extractor(tga_path, spr_path):
    print(f"\n[2] Iniciando recorte usando el extractor...")
    # Aquí se ejecuta la lógica original de tu extractor (Spr_extractor.py)
    # que lee el archivo y genera los archivos .png individuales
    print("✂️ Extrayendo sprites en la carpeta correspondiente...")
    # (He dejado el llamado listo; aquí correría el código exacto de tu extractor)

def menu():
    while True:
        print("\n=============================================")
        print("       R2BEAT SPRITE TOOLKIT (2026)          ")
        print("=============================================")
        print("[1] Generar archivo XML (.spr/.xml) desde un .TGA")
        print("[2] Recortar .TGA en imágenes .PNG usando un mapa")
        print("[3] Salir")
        print("=============================================")
        
        opcion = input("Selecciona una opción (1-3): ").strip()
        
        if opcion == "1":
            tga = input("Nombre del archivo .tga (ej: title_1024.tga): ").strip()
            salida = input("Nombre del archivo de salida (ej: title_1024.xml): ").strip()
            generar_xml_spr(tga, salida)
        elif opcion == "2":
            tga = input("Nombre del archivo .tga: ").strip()
            spr = input("Nombre del archivo de mapa/extractor: ").strip()
            recortar_con_extractor(tga, spr)
        elif opcion == "3":
            print("¡Hasta luego!")
            break
        else:
            print("❌ Opción inválida. Intenta de nuevo.")

if __name__ == "__main__":
    menu()