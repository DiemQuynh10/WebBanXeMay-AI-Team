# AI-Powered Motorcycle E-Commerce Platform

An ASP.NET Core MVC motorcycle e-commerce platform integrated with an AI chatbot, Retrieval-Augmented Generation (RAG), conversation memory, and Telegram Bot support.

---

## Project Overview

This project combines a complete motorcycle sales website with an AI-powered customer support system.

The chatbot is designed to handle both structured business operations and AI-assisted conversations, including:

- Product recommendation
- Product comparison
- Inventory lookup
- Order lookup
- Context-aware conversations
- Multi-turn interactions
- Telegram Bot support

---

## Highlights

### AI Chatbot

- Intent Routing Architecture
- Conversation Memory
- Multi-turn Context Handling
- Product Recommendation
- Product Comparison
- Inventory Checking
- Order Tracking

### Retrieval-Augmented Generation (RAG)

- ChromaDB Vector Database
- Semantic Retrieval
- Knowledge Grounding
- Context Injection

### Real-Time Business Integration

- Product Database Lookup
- Inventory Query
- Order Status Tracking
- Customer Information Retrieval

### Multi-Channel Support

- Web Chat Widget
- Telegram Bot

---

# Screenshots

## Home Page

![Home Page](docs/homepage.png)

---

## AI Chatbot - Product Recommendation

![Recommendation](docs/chatbot-recommendation.png)

Example:

> Recommend a motorcycle for women around 30 million VND.

---

## AI Chatbot - Product Comparison

![Comparison](docs/chatbot-comparison.png)

Example:

> Compare Honda Vision and Yamaha Latte.

---

## AI Chatbot - Order Lookup

![Order Lookup](docs/order-lookup.png)

Example:

> Lookup order #1041.

---

## Admin Dashboard

![Dashboard](docs/admin-dashboard.png)

---

# System Architecture

![Architecture](docs/architecture.png)

### Request Flow

```text
User
 ↓
Chat Controller
 ↓
Chat Orchestrator
 ↓
Intent Detection
 ↓
Decision Engine
 ├─ Tool API
 ├─ Conversation Memory
 └─ LLM Service
        ↓
       RAG
        ↓
     OpenAI
 ↓
Response Generation
 ↓
User
```

---

# Chatbot Capabilities

## Product Recommendation

Examples:

- Recommend a motorcycle under 30 million VND
- Recommend a motorcycle for women
- Recommend a fuel-efficient motorcycle

---

## Product Comparison

Examples:

- Compare Honda Vision and Yamaha Latte
- Compare the first and second motorcycles
- Compare Vision with Air Blade

---

## Inventory Lookup

Examples:

- Is Honda Vision available?
- Check stock for Yamaha Grande

---

## Order Lookup

Examples:

- Lookup order #1041
- Check my order status

---

## Context-Aware Conversation

Examples:

```text
Recommend motorcycles around 30 million

→ Honda Vision
→ Yamaha Latte

Compare the first and second one

→ Chatbot understands the previous context
```

---

# Technology Stack

## Backend

- ASP.NET Core MVC
- C#
- Entity Framework Core
- SQL Server

## AI Components

- OpenAI API
- Retrieval-Augmented Generation (RAG)
- ChromaDB
- Conversation Memory
- Intent Routing
- Chat Orchestrator

## Frontend

- Razor Views
- Bootstrap
- JavaScript
- AJAX

## Integration

- Telegram Bot API

---

# Key Features

## Customer Features

- Browse motorcycle catalog
- Product search and filtering
- Product details
- Shopping cart
- Order placement
- Order tracking
- AI chatbot support

## Admin Features

- Dashboard & analytics
- Product management
- Order management
- Voucher management
- Review moderation
- Customer consultation management

---

# AI Architecture Design

The chatbot follows a hybrid architecture:

### Rule-Based Layer

Handles:

- Product lookup
- Inventory lookup
- Order lookup
- Structured business operations

### AI Layer

Handles:

- User intent understanding
- Natural language interaction
- Recommendation generation
- Contextual conversations

### RAG Layer

Provides:

- Knowledge retrieval
- Semantic search
- Context grounding

This design reduces hallucination while maintaining flexibility for natural conversations.

---

# Repository Structure

```text
WebBanXeMay
│
├── WebBanXeMay/              Main MVC Application
├── Chatbot.API/              AI Chatbot Service
├── Chatbot.API.Tests/        Unit Tests
├── docs/                     README Images
│
├── README.md
└── WebBanXeMay.sln
```

---

# Author

Graduation Project

AI-Powered Motorcycle E-Commerce Platform

ASP.NET Core MVC + OpenAI + RAG + ChromaDB + Telegram Bot
