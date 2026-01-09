# 📦 Projet Odoo – Application Web & API JSON-RPC

## 🧾 Description du projet

Ce projet a pour objectif de démontrer l’utilisation de l’**API JSON-RPC d’Odoo** à travers une **application web développée en C# (ASP.NET Core MVC)**.

L’application permet :

* de se connecter à une instance Odoo locale,
* de consulter un catalogue de produits,
* de créer une commande client,
* de suivre l’état d’une commande créée.

Le projet s’appuie sur :

* **Odoo 18** (conteneur Docker),
* **PostgreSQL** (base de données Odoo),
* **ASP.NET Core MVC** pour l’interface web,
* l’API **JSON-RPC Odoo** (`/web/session/authenticate`, `/web/dataset/call_kw`).

---

## 🛠️ Technologies utilisées

* Odoo 18
* PostgreSQL
* Docker & Docker Compose
* C# – ASP.NET Core MVC
* JSON-RPC 2.0
* HTML / Razor / Bootstrap

---

## 📁 Structure du projet

```
odoo_lab/
├── docker-compose.yml
├── addons/                # Addons Odoo personnalisés
├── WebApp/                # Application web ASP.NET Core
│   ├── Controllers/
│   ├── Views/
└── README.md
```

---

## 🚀 Lancement du projet

### 1️⃣ Démarrer Odoo avec Docker

Depuis la racine du projet :

```bash
docker compose up -d
```

L’interface Odoo est alors accessible sur :
👉 `http://localhost:8069`

---

### 2️⃣ Configuration Odoo (une seule fois)

* Créer une base de données nommée : **`odoo_lab`**
* Activer les applications nécessaires (Ventes, Produits)
* Créer ou utiliser l’utilisateur `admin`
* suivre les étapes du fichier : ODOO_GUIDE.PDF

---

### 3️⃣ Lancer l’application Web

Depuis le dossier `WebApp` :

```bash
dotnet run
```

L’application est accessible sur l’URL affichée dans le terminal
(ex. `http://localhost:5227`).

---

## ⚙️ Configuration par défaut de l’application

Dans l’interface web, les valeurs par défaut sont :

* **URL Odoo** : `http://localhost:8069`
* **Base de données** : `odoo_lab`
* **Utilisateur** : `admin`
* **Mot de passe** : `admin`

👉 Ces valeurs peuvent être modifiées directement dans le formulaire si nécessaire.

---

## 🔗 Fonctionnalités implémentées

### ✔️ Connexion à Odoo

* Authentification via `/web/session/authenticate`
* Gestion de session via cookie `session_id`

### ✔️ Consultation du catalogue produits

* Appel JSON-RPC `/web/dataset/call_kw`
* Méthode `search_read` sur le modèle `product.template`
* Affichage des informations principales (nom, prix, stock, catégorie)

### ✔️ Création de commande client

* Création d’une commande (`sale.order`)
* Ajout d’une ligne de commande (`sale.order.line`)
* Gestion de la quantité sélectionnée

### ✔️ Suivi de commande

* Affichage des informations de la commande créée :

  * client
  * date
  * statut
  * total
  * lignes de commande

---

## 🧠 Points pédagogiques abordés

* Utilisation d’une API JSON-RPC
* Communication backend ↔ ERP
* Gestion de session et authentification
* Structuration d’une application web MVC
* Manipulation de données métier (produits, commandes)

---

## 📌 Remarques

* Le projet est prévu pour fonctionner **en local**.
* L’utilisation de Docker garantit la reproductibilité de l’environnement.
* Le nom de la base de données par défaut est **`odoo_lab`**.

---
