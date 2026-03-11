#!/usr/bin/env python3
"""
Prueba de extracción IA con URL real de Sherpa
Combinación: USA → MEX (Estados Unidos a México)
"""

import asyncio
import base64
import json
import sys
from datetime import datetime, timedelta
from playwright.async_api import async_playwright
import aiohttp

# Configuración
OLLAMA_URL = "http://192.168.5.91:11434/api/chat"
MODEL = "bakllava:latest"
ORIGEN = "USA"
DESTINO = "MEX"
IDIOMA = "en-US"

async def construir_url():
    """Construye URL de Sherpa con parámetros"""
    fecha_base = datetime.now()
    departure_date = (fecha_base + timedelta(days=15)).strftime("%Y-%m-%d")
    return_date = (fecha_base + timedelta(days=22)).strftime("%Y-%m-%d")
    
    params = {
        "language": IDIOMA,
        "nationality": ORIGEN,
        "originCountry": ORIGEN,
        "departureDate": departure_date,
        "returnDate": return_date,
        "travelPurposes": "TOURISM",
        "tripType": "roundTrip",
        "fullyVaccinated": "true",
        "affiliateId": "sherpa"
    }
    
    query = "&".join([f"{k}={v}" for k, v in params.items()])
    return f"https://apply.joinsherpa.com/travel-restrictions/{DESTINO}?{query}"

async def capturar_pagina():
    """Usa Playwright para navegar y capturar screenshot"""
    url = await construir_url()
    
    print(f"🌐 Navegando a: {url}")
    print("   (Esto puede tomar 10-20 segundos...)\n")
    
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True)
        page = await browser.new_page(viewport={"width": 1920, "height": 1080})
        
        try:
            response = await page.goto(url, wait_until="networkidle", timeout=30000)
            
            if response.status != 200:
                print(f"❌ Error HTTP {response.status}")
                return None, None
            
            print(f"✅ Página cargada (HTTP {response.status})")
            
            # Esperar carga de contenido dinámico
            await asyncio.sleep(5)
            
            # Tomar screenshot
            print("📸 Capturando screenshot...")
            screenshot = await page.screenshot(full_page=True, type="png")
            
            # Obtener HTML
            html = await page.content()
            
            print(f"   Screenshot: {len(screenshot) / 1024:.1f} KB")
            print(f"   HTML: {len(html) / 1024:.1f} KB\n")
            
            return screenshot, html
            
        finally:
            await browser.close()

async def extraer_con_ia(screenshot_bytes, html_content):
    """Envía screenshot a bakllava para análisis"""
    
    screenshot_b64 = base64.b64encode(screenshot_bytes).decode('utf-8')
    html_truncado = html_content[:4000] if len(html_content) > 4000 else html_content
    
    prompt = f"""Analiza esta página de requisitos de viaje de Sherpa.

CONTEXTO:
- Viajero de nacionalidad: {ORIGEN} (Estados Unidos)
- Destino: {DESTINO} (México)
- Idioma de la página: {IDIOMA}

EXTRAER información en formato JSON:
{{
  "requiere_visa": true/false/null,
  "tipo_visa": "descripción o null",
  "validez_pasaporte": "meses requeridos o null",
  "vacunas_requeridas": ["lista"],
  "requisitos_sanitarios": "descripción o null",
  "documentos_adicionales": ["lista"],
  "advertencias": ["lista"],
  "notas": "información adicional"
}}

REGLAS:
- Analiza SOLO la información visible en la imagen
- Si no hay información clara, usa null
- NO inventes datos que no estén en la imagen
- Responde SOLO con el JSON, sin explicaciones adicionales"""

    payload = {
        "model": MODEL,
        "messages": [
            {
                "role": "system",
                "content": "Eres un experto en extracción de requisitos de viaje. Responde SOLO con JSON válido."
            },
            {
                "role": "user",
                "content": prompt,
                "images": [screenshot_b64]
            }
        ],
        "stream": False,
        "options": {
            "temperature": 0.1,
            "num_predict": 1024
        }
    }
    
    print(f"🤖 Enviando a {MODEL} para análisis...")
    print(f"   URL: {OLLAMA_URL}")
    print("   (Esto puede tomar 10-30 segundos con GPU RTX 5070 Ti)\n")
    
    start_time = datetime.now()
    
    async with aiohttp.ClientSession() as session:
        async with session.post(OLLAMA_URL, json=payload, timeout=120) as response:
            result = await response.json()
    
    duration = (datetime.now() - start_time).total_seconds()
    
    print(f"✅ Respuesta recibida en {duration:.1f} segundos\n")
    
    return result

def parsear_resultado(content):
    """Intenta extraer JSON de la respuesta"""
    try:
        # Buscar JSON en la respuesta
        content = content.strip()
        
        # Si viene con markdown code block
        if "```json" in content:
            content = content.split("```json")[1].split("```")[0].strip()
        elif "```" in content:
            content = content.split("```")[1].split("```")[0].strip()
        
        return json.loads(content)
    except:
        return {"error": "No se pudo parsear JSON", "raw": content}

async def main():
    print("=" * 60)
    print("🧪 PRUEBA DE EXTRACCIÓN IA CON URL REAL DE SHERPA")
    print("=" * 60)
    print(f"Combinación: {ORIGEN} (Estados Unidos) → {DESTINO} (México)")
    print(f"Modelo: {MODEL}")
    print(f"Ollama: {OLLAMA_URL}")
    print("=" * 60)
    print()
    
    try:
        # Paso 1: Capturar página
        screenshot, html = await capturar_pagina()
        
        if screenshot is None:
            print("❌ No se pudo capturar la página")
            sys.exit(1)
        
        # Paso 2: Extraer con IA
        resultado = await extraer_con_ia(screenshot, html)
        
        # Paso 3: Mostrar resultados
        content = resultado.get("message", {}).get("content", "")
        
        print("📋 RESULTADO DE LA EXTRACCIÓN:")
        print("=" * 60)
        
        datos = parsear_resultado(content)
        
        if "error" in datos:
            print(f"⚠️  {datos['error']}")
            print(f"\nRespuesta cruda:\n{datos.get('raw', 'N/A')}")
        else:
            print(json.dumps(datos, indent=2, ensure_ascii=False))
        
        print("=" * 60)
        
        # Métricas
        print("\n📊 MÉTRICAS:")
        print(f"   Total duration: {resultado.get('total_duration', 0) / 1e9:.2f}s")
        print(f"   Load duration: {resultado.get('load_duration', 0) / 1e9:.2f}s")
        print(f"   Prompt eval: {resultado.get('prompt_eval_count', 0)} tokens")
        print(f"   Response: {resultado.get('eval_count', 0)} tokens")
        
        print("\n✅ Prueba completada exitosamente!")
        
    except Exception as e:
        print(f"\n❌ Error: {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)

if __name__ == "__main__":
    asyncio.run(main())
