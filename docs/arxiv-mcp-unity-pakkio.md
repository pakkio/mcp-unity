# arXiv:2608.14023v1 [cs.SE] 12 Aug 2026

# LLM-Driven Real-Time Engine Control and Autonomous 3D Vehicle Assembly: An Extended Model Context Protocol (MCP) Framework for Unity

* **Author**: Claudio Pacchiega (Pakkio)$^1$
* **Affiliation**: $^1$Pakkio Unity AI Research Labs
* **Official Paper PDF**: 📄 [Download / View arXiv PDF Paper (343 KB)](file:///C:/Users/claudio.pacchiega/w/mcp-unity/docs/arxiv-mcp-unity-pakkio.pdf)
* **LaTeX Source**: 📐 [LaTeX .tex Source](file:///C:/Users/claudio.pacchiega/w/mcp-unity/docs/arxiv-mcp-unity-pakkio.tex)

---

## Abstract

Interfacing Large Language Model (LLM) agents with real-time 3D game engines poses significant technical challenges regarding main-thread synchronicity, scene graph mutations, and spatial physics stability. In this paper, we present an extended implementation of the Model Context Protocol (MCP) for the Unity Editor (`pakkio/mcp-unity`). We address critical engine-level failure modes of vanilla stdio-WebSocket bridges, including deferred frame rendering, sibling hierarchy order loss, and scaled transform cloning corruption. Furthermore, we introduce an unsupervised spatial bounds clustering algorithm for automated feature identification on generic 3D meshes (e.g., glTF/GLB models from Sketchfab), coordinate axis decoupling for Blender-imported assets, and Rigidbody inertia tensor stabilization. Finally, we validate our system through a closed-loop orbital navigation benchmark in Play Mode, achieving precise $233.1^\circ$ trajectory control and real-time telemetry extraction.

**Keywords**: Model Context Protocol, Unity Engine, Autonomous 3D Assembly, Spatial Clustering, Vehicle Dynamics, Real-Time Telemetry.

---

## 1. Introduction

The integration of generative artificial intelligence into interactive 3D content creation tools is shifting software development from manual GUI manipulation to high-level intent-based agentic workflows [1]. The Model Context Protocol (MCP) provides a standardized client-server architecture enabling AI agents to inspect resources, execute tool invocations, and modify state across external applications.

---

## References

1. Anthropic, *Model Context Protocol Specification*, 2024.
2. Unity Technologies, *Unity Editor Scripting and WheelCollider Physics Manual*, 2023.
3. A. Atteneder, *glTFast: High-Performance glTF Import Solution for Unity*, 2023.
