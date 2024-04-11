# Real-time Seamless Object Space Shading

![](teaser.png)

## Introduction
- This repo includes the source code for the following Eurographics 2024 short paper

> **Real-time Seamless Object Space Shading**<br>
> Tianyu Li*, Xiaoxin Guo*<br>
> (*Joint first authors) <br>

Object space shading remains a challenging problem in real-time rendering due to runtime overhead and object parameterization limitations. While the recently developed algorithm by Baker et al. enables high-performance real-time object
space shading, it still suffers from seam artifacts. In this paper, we introduce an innovative object space shading system leveraging a virtualized per-halfedge texturing schema to obviate excessive shading and preclude texture seam artifacts. Moreover,
we implement ReSTIR GI on our system, removing the necessity of temporally reprojecting shading samples,
improving the convergence of areas of disocclusion. Our system yields superior results in terms of both efficiency and visual fidelity.

## Prerequisites
- Windows 10 version 20H2 or newer
- Visual Studio 2019 or higher
- Windows 10 SDK version 10.0.19041.1 Or newer
- RTX 2060 or higher (Graphics card with raytracing support)
- Unity 2023.2 or newer