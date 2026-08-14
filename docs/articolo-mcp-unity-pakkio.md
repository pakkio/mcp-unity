# Come la Versione "Taroccata" di Pakkio Rivoluziona MCP Unity: Automazione 3D, Fix dell'Engine e Fisica Veicoli con l'IA

* **Autore**: Claudio Pacchiega (Pakkio) \& AI Pair Programmer
* **Data**: 12 Agosto 2026
* **Repository**: [`pakkio/mcp-unity`](file:///C:/Users/claudio.pacchiega/w/mcp-unity/AGENTS.md)
* **Documento PDF Ufficiale**: 📄 [Scarica / Visualizza la Guida Master PDF](file:///C:/Users/claudio.pacchiega/w/mcp-unity/docs/guida-mcp-unity-pakkio-macchina.pdf)

---

## 📌 Introduzione

Integrare gli agenti di Intelligenza Artificiale direttamente all'interno dei motori di gioco 3D come **Unity Editor** è una delle frontiere più promettenti dello sviluppo moderno. Il protocollo **MCP (Model Context Protocol)** permette a modelli LLM ed agenti autonomi di "guidare" l'Editor eseguendo comandi, modificando oggetti e leggendo la telemetria di scena.

Tuttavia, chi ha provato ad utilizzare la versione standard del pacchetto MCP per Unity si è scontrato rapidamente con limiti storici dell'Engine: **la Scene View che non si aggiorna se non si muove il mouse**, **la gerarchia che perde l'ordine visivo quando si riordinano gli oggetti**, **memory leak sui log di console** e **corruzioni di trasformazione quando si duplicano nodi figli scalati**.

Per superare queste barriere, **Claudio Pacchiega (Pakkio)** ha realizzato una versione fortemente ottimizzata e "taroccata" ([`pakkio/mcp-unity`](file:///C:/Users/claudio.pacchiega/w/mcp-unity/AGENTS.md)) che trasforma l'esperienza di sviluppo assistito dall'IA in Unity.

---

## 🚀 Le 4 Macro-Migliorie del Fork Pakkio

```
[ Client MCP (es. Antigravity / Claude) ] 
                 ↕ (stdio / MCP SDK)
     [ Server Node.js (Server~/src/index.ts) ]
                 ↕ (WebSocket JSON-RPC / ws://localhost:8090/McpUnity)
 [ Unity Editor (Editor/UnityBridge/McpUnityServer.cs con Repaint Fix) ]
```

### 1. 🛠️ Nuovi Tool Esclusivi
* **`set_sibling_index`**: In Unity, la chiamata `Transform.SetParent` non modifica l'indice dei nodi fratelli. Il nuovo tool consente di riordinare la gerarchia visiva in modalità relativa (`insertBeforeInstanceId`, `insertAfterInstanceId`) o assoluta.
* **`capture_screenshot`**: Consente all'agente AI di scattare screenshot in alta definizione della Game View o della Scene View per "vedere" visivamente il risultato delle modifiche apportate.
* **`export_package`**: Esporta asset e scene in un pacchetto `.unitypackage` riutilizzabile.
* **Supporto Riferimenti in `update_component`**: Gestione nativa di `instanceId` e `objectPath` verso componenti ed array di oggetti di scena.

### 2. 🐛 Fix Critici dell'Engine Unity
* **Scene Repaint Fix**: Aggiunte le chiamate `SceneView.RepaintAll()` ed `EditorApplication.QueuePlayerLoopUpdate()`. Ora le modifiche effettuate dall'IA si ridisegnano **istantaneamente** sullo schermo senza dover interagire manualmente con l'interfaccia.
* **Fix Duplicazione Nodi Scalati (`duplicate_gameobject`)**: Risolto il bug per cui duplicare oggetti sotto padri ruotati o scalati causava un posizionamento del clone a coordinate errate.
* **Gestione Pacchetti Git e File `.meta`**: Generazione automatica dei GUID `.meta` per evitare che Unity ignori in silenzio nuovi file C# aggiunti nei package distribuiti via Git.

### 3. ⚡ Prestazioni & Gestione della Memoria
* **Ring Buffer per i Log di Console**: Previene memory leak nelle sessioni prolungate sostituendo gli elenchi illimitati con un buffer circolare ad alte prestazioni.
* **Reflection & Shader Caching**: Caching dei parametri dei materiali e delle reflection C# per azzerare i tempi di latenza nelle chiamate WebSocket.

---

## 🏎️ Case Study: Importazione Auto da Sketchfab e Fisica in Play Mode

Nel manuale pratico allegato è stato documentato l'intero ciclo di vita realizzativo per importare un'automobile 3D da Sketchfab e trasformarla in un veicolo guidabile in Play Mode:

1. **Importazione GLB/glTF**: Ricerca della *1975 Porsche 911 (930) Turbo* e decodifica istantanea via package `com.atteneder.gltfast`.
2. **Algoritmo di Clustering Bounds**: I modelli Sketchfab presentano nodi con nomi generici (`Circle.050_51`). Tramite il calcolo del volume dei `MeshRenderer.bounds`, l'agente AI riconosce matematicamente le 4 ruote posizionate ai vertici del bounding box e le rinomina in lingua italiana (`Ruota_Anteriore_Sinistra`, ecc.).
3. **Fix Rotazione glTF a 270°**: Disaccoppiamento della mesh dal nodo radice neutro `(0,0,0)` per impedire che l'orientamento di Blender (-90° asse X) corrompa i vettori della fisica.
4. **Tensore d'Inerzia**: Iniezione del Box Collider carrozzeria ed abbassamento del Center of Mass (CoM a `y = -0.2`) per garantire la stabilità del `Rigidbody`.

---

## ⭕ Il Test della Rotazione Attorno al Cubo Rosso in Play Mode

Per validare la fisica, è stata eseguita una prova di guida circolare in tempo reale:
* **Espansione del Terreno**: Piano stradale esteso a **80×80 metri** (scala `8, 1, 8`).
* **Cubo Rosso Landmark**: Posizionato al centro `(0,0,0)`.
* **Telemetria dell'Angolo Yaw**: Campionamento dell'imbardata durante l'orbita ad alta velocità:
  $$\theta(t) = 0.0^\circ \longrightarrow -18.6^\circ \longrightarrow 233.1^\circ$$

---

## 📄 Risorse e Download Documenti

| Formato | Descrizione | Collegamento Diretto |
| :--- | :--- | :--- |
| **PDF** | Documento di specifica completo impaginato in alta risoluzione | 📄 [Apri PDF (930 KB)](file:///C:/Users/claudio.pacchiega/w/mcp-unity/docs/guida-mcp-unity-pakkio-macchina.pdf) |
| **HTML** | Modello interattivo con filtri ruoli (TU / CLAUDE / UNITY) e ricerca | 🌐 [Apri HTML Interattivo](file:///C:/Users/claudio.pacchiega/w/mcp-unity/docs/guida-mcp-unity-pakkio-macchina.html) |
| **LaTeX** | Sorgente `.tex` con equazioni matematiche per volume ed inerzia | 📐 [Apri Sorgente .tex](file:///C:/Users/claudio.pacchiega/w/mcp-unity/docs/manuale-mcp-unity-pakkio.tex) |

---

*Articolo generato per la documentazione del fork `pakkio/mcp-unity`.*
