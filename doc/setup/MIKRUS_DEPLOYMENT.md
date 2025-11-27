# 🐋 Deployment na mikr.us (VPS) - Docker Compose

Kompletny przewodnik publikacji SportRental na polskim VPS mikr.us z użyciem Docker.

---

## 📋 **Spis treści**

1. [Przegląd](#przegląd)
2. [Wymagania zasobów](#wymagania-zasobów)
3. [Przygotowanie projektu](#przygotowanie-projektu)
4. [Konfiguracja serwera](#konfiguracja-serwera)
5. [Deployment](#deployment)
6. [Monitorowanie](#monitorowanie)
7. [Backup](#backup)

---

## 🏗️ **Przegląd**

### **Plan mikr.us Mikrus 3.5**

| Specyfikacja | Wartość |
|--------------|---------|
| **RAM** | 4 GB |
| **CPU** | Shared (fair-use) |
| **Dysk** | 40 GB SSD |
| **Transfer** | Nielimitowany |
| **Cena** | **~197 zł/rok** (~16.40 zł/miesiąc) |
| **IP** | 1x IPv4 + IPv6 |
| **Lokalizacja** | Polska (niska latencja) |

**🔗 Link:** https://mikr.us

---

## 💾 **Wymagania zasobów - SportRental**

### **Architektura:**

```
┌─────────────────────────────────────────────────────┐
│             MIKR.US VPS (4GB RAM)                   │
├─────────────────────────────────────────────────────┤
│                                                     │
│  ┌──────────────┐  ┌──────────────┐               │
│  │   nginx      │  │  Admin Panel │               │
│  │   (proxy)    │  │  (~800MB)    │               │
│  │   (50MB)     │  └──────────────┘               │
│  └──────────────┘                                  │
│         │                                           │
│         ├───► ┌──────────────┐                    │
│         │     │  Public API  │                    │
│         │     │  (~400MB)    │                    │
│         │     └──────────────┘                    │
│         │                                          │
│         ├───► ┌──────────────┐                   │
│         │     │ Media Storage│                   │
│         │     │  (~400MB)    │                   │
│         │     └──────────────┘                   │
│         │                                         │
│         └───► ┌──────────────┐                  │
│               │ WASM Client  │                  │
│               │ (static ~5MB)│                  │
│               └──────────────┘                  │
│                                                  │
│  ┌────────────────────────────┐                │
│  │      PostgreSQL 14         │                │
│  │       (~1GB RAM)           │                │
│  └────────────────────────────┘                │
│                                                 │
│  Total RAM usage: ~2.6-3.5 GB (w szczycie)    │
│  Available margin: ~500MB-1.4GB                │
└─────────────────────────────────────────────────┘
```

### **Szacowane zużycie zasobów:**

| Komponent | RAM (idle) | RAM (peak) | CPU | Dysk |
|-----------|------------|------------|-----|------|
| **nginx** | 10-50 MB | 100 MB | 1-5% | ~100 MB |
| **Admin Panel** | 300 MB | 800 MB | 10-30% | ~200 MB |
| **Public API** | 150 MB | 400 MB | 5-20% | ~150 MB |
| **Media Storage** | 150 MB | 400 MB | 5-15% | ~150 MB |
| **WASM Client** | 0 MB | 0 MB | 0% | ~5 MB |
| **PostgreSQL** | 256 MB | 1 GB | 5-20% | ~2-10 GB |
| **Docker** | 100 MB | 200 MB | 2-5% | ~500 MB |
| **System (Ubuntu)** | 150 MB | 300 MB | 2-5% | ~5 GB |
| **RAZEM** | **~1.1 GB** | **~3.2 GB** | **30-100%** | **~8-16 GB** |

### **✅ WERDYKT: 4GB RAM WYSTARCZY!**

**Dla ruchu:**
- ✅ **1-10 użytkowników jednocześnie** - bez problemu
- ✅ **10-50 użytkowników jednocześnie** - komfortowo
- ⚠️ **50-100 użytkowników jednocześnie** - możliwe spowolnienia
- ❌ **>100 użytkowników jednocześnie** - potrzebny upgrade

**💡 Optymalizacje:**
- Wyłącz Admin Panel gdy nie jest używany (oszczędność ~800MB)
- Użyj zewnętrznego blob storage zamiast local (oszczędność RAM)
- PostgreSQL connection pooling (mniej RAM per connection)

---

## 🐳 **Przygotowanie projektu**

### **KROK 1: Utwórz Dockerfile dla każdej aplikacji**

#### **1.1. Admin Panel**

```dockerfile
# SportRental.Admin/Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["SportRental.Admin/SportRental.Admin.csproj", "SportRental.Admin/"]
COPY ["SportRental.Infrastructure/SportRental.Infrastructure.csproj", "SportRental.Infrastructure/"]
COPY ["SportRental.Shared/SportRental.Shared.csproj", "SportRental.Shared/"]
RUN dotnet restore "SportRental.Admin/SportRental.Admin.csproj"
COPY . .
WORKDIR "/src/SportRental.Admin"
RUN dotnet build "SportRental.Admin.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SportRental.Admin.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SportRental.Admin.dll"]
```

#### **1.2. Public API**

```dockerfile
# SportRental.Api/Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["SportRental.Api/SportRental.Api.csproj", "SportRental.Api/"]
COPY ["SportRental.Infrastructure/SportRental.Infrastructure.csproj", "SportRental.Infrastructure/"]
COPY ["SportRental.Shared/SportRental.Shared.csproj", "SportRental.Shared/"]
RUN dotnet restore "SportRental.Api/SportRental.Api.csproj"
COPY . .
WORKDIR "/src/SportRental.Api"
RUN dotnet build "SportRental.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SportRental.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SportRental.Api.dll"]
```

#### **1.3. Media Storage**

```dockerfile
# SportRental.MediaStorage/Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["SportRental.MediaStorage/SportRental.MediaStorage.csproj", "SportRental.MediaStorage/"]
COPY ["SportRental.Infrastructure/SportRental.Infrastructure.csproj", "SportRental.Infrastructure/"]
RUN dotnet restore "SportRental.MediaStorage/SportRental.MediaStorage.csproj"
COPY . .
WORKDIR "/src/SportRental.MediaStorage"
RUN dotnet build "SportRental.MediaStorage.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SportRental.MediaStorage.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SportRental.MediaStorage.dll"]
```

#### **1.4. WASM Client**

```dockerfile
# SportRental.Client/Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["SportRental.Client/SportRental.Client.csproj", "SportRental.Client/"]
COPY ["SportRental.Shared/SportRental.Shared.csproj", "SportRental.Shared/"]
RUN dotnet restore "SportRental.Client/SportRental.Client.csproj"
COPY . .
WORKDIR "/src/SportRental.Client"
RUN dotnet publish "SportRental.Client.csproj" -c Release -o /app/publish

FROM nginx:alpine AS final
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY SportRental.Client/nginx.conf /etc/nginx/nginx.conf
EXPOSE 80
```

**nginx.conf dla WASM:**
```nginx
# SportRental.Client/nginx.conf
events { }
http {
    include mime.types;
    types {
        application/wasm wasm;
    }

    server {
        listen 80;
        
        location / {
            root /usr/share/nginx/html;
            try_files $uri $uri/ /index.html =404;
        }
    }
}
```

---

### **KROK 2: Docker Compose**

```yaml
# docker-compose.yml (w root projektu)
version: '3.8'

services:
  postgres:
    image: postgres:14-alpine
    container_name: sportrental-db
    environment:
      POSTGRES_DB: sportrental
      POSTGRES_USER: sportadmin
      POSTGRES_PASSWORD: ${DB_PASSWORD}
    volumes:
      - postgres_data:/var/lib/postgresql/data
    ports:
      - "5432:5432"
    restart: unless-stopped
    deploy:
      resources:
        limits:
          memory: 1G
        reservations:
          memory: 256M

  admin:
    build:
      context: .
      dockerfile: SportRental.Admin/Dockerfile
    container_name: sportrental-admin
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8080
      - ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Database=sportrental;Username=sportadmin;Password=${DB_PASSWORD}
      - Stripe__SecretKey=${STRIPE_SECRET_KEY}
      - Stripe__PublishableKey=${STRIPE_PUBLISHABLE_KEY}
      - Jwt__Secret=${JWT_SECRET}
      - Email__Smtp__Host=${EMAIL_HOST}
      - Email__Smtp__Port=${EMAIL_PORT}
      - Email__Smtp__Username=${EMAIL_USERNAME}
      - Email__Smtp__Password=${EMAIL_PASSWORD}
      - Email__From=${EMAIL_FROM}
    depends_on:
      - postgres
    ports:
      - "5001:8080"
    restart: unless-stopped
    deploy:
      resources:
        limits:
          memory: 1G
        reservations:
          memory: 300M

  api:
    build:
      context: .
      dockerfile: SportRental.Api/Dockerfile
    container_name: sportrental-api
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8080
      - ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Database=sportrental;Username=sportadmin;Password=${DB_PASSWORD}
      - Stripe__SecretKey=${STRIPE_SECRET_KEY}
      - Stripe__WebhookSecret=${STRIPE_WEBHOOK_SECRET}
      - Jwt__Secret=${JWT_SECRET}
    depends_on:
      - postgres
    ports:
      - "5002:8080"
    restart: unless-stopped
    deploy:
      resources:
        limits:
          memory: 512M
        reservations:
          memory: 150M

  media:
    build:
      context: .
      dockerfile: SportRental.MediaStorage/Dockerfile
    container_name: sportrental-media
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8080
      - Storage__Type=FileSystem
      - Storage__FileSystem__BasePath=/app/uploads
    volumes:
      - media_data:/app/uploads
    ports:
      - "5003:8080"
    restart: unless-stopped
    deploy:
      resources:
        limits:
          memory: 512M
        reservations:
          memory: 150M

  client:
    build:
      context: .
      dockerfile: SportRental.Client/Dockerfile
    container_name: sportrental-client
    ports:
      - "5004:80"
    restart: unless-stopped
    deploy:
      resources:
        limits:
          memory: 128M
        reservations:
          memory: 10M

  nginx:
    image: nginx:alpine
    container_name: sportrental-nginx
    volumes:
      - ./nginx/nginx.conf:/etc/nginx/nginx.conf:ro
      - ./nginx/ssl:/etc/nginx/ssl:ro
    ports:
      - "80:80"
      - "443:443"
    depends_on:
      - admin
      - api
      - media
      - client
    restart: unless-stopped
    deploy:
      resources:
        limits:
          memory: 128M
        reservations:
          memory: 10M

volumes:
  postgres_data:
  media_data:
```

---

### **KROK 3: Nginx Reverse Proxy**

```nginx
# nginx/nginx.conf
events {
    worker_connections 1024;
}

http {
    # Rate limiting
    limit_req_zone $binary_remote_addr zone=api_limit:10m rate=10r/s;
    limit_req_zone $binary_remote_addr zone=general_limit:10m rate=30r/s;

    # Upstream definitions
    upstream admin_backend {
        server admin:8080;
    }

    upstream api_backend {
        server api:8080;
    }

    upstream media_backend {
        server media:8080;
    }

    upstream client_backend {
        server client:80;
    }

    # HTTP Server (redirect to HTTPS)
    server {
        listen 80;
        server_name _;
        
        # Allow Let's Encrypt validation
        location /.well-known/acme-challenge/ {
            root /var/www/certbot;
        }

        # Redirect everything else to HTTPS
        location / {
            return 301 https://$host$request_uri;
        }
    }

    # HTTPS Server
    server {
        listen 443 ssl http2;
        server_name twoja-domena.pl;

        # SSL certificates (Let's Encrypt)
        ssl_certificate /etc/nginx/ssl/fullchain.pem;
        ssl_certificate_key /etc/nginx/ssl/privkey.pem;
        ssl_protocols TLSv1.2 TLSv1.3;
        ssl_ciphers HIGH:!aNULL:!MD5;

        # Admin Panel
        location /admin/ {
            proxy_pass http://admin_backend/;
            proxy_http_version 1.1;
            proxy_set_header Upgrade $http_upgrade;
            proxy_set_header Connection "upgrade";
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
            proxy_cache_bypass $http_upgrade;
            
            # WebSocket support for Blazor SignalR
            proxy_read_timeout 300s;
            proxy_send_timeout 300s;
        }

        # Public API
        location /api/ {
            limit_req zone=api_limit burst=20 nodelay;
            
            proxy_pass http://api_backend/api/;
            proxy_http_version 1.1;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
        }

        # Media Storage
        location /media/ {
            limit_req zone=general_limit burst=50 nodelay;
            
            proxy_pass http://media_backend/;
            proxy_http_version 1.1;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            
            # Large file uploads
            client_max_body_size 100M;
        }

        # WASM Client (default)
        location / {
            limit_req zone=general_limit burst=50 nodelay;
            
            proxy_pass http://client_backend/;
            proxy_http_version 1.1;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
        }
    }
}
```

---

## 🚀 **Deployment krok po kroku**

### **KROK 1: Zakup VPS na mikr.us**

1. Wejdź na https://mikr.us
2. Wybierz **Mikrus 3.5** (4GB RAM, 40GB SSD, ~197 zł/rok)
3. Zarejestruj się i opłać
4. Otrzymasz email z danymi dostępowymi:
   - IP serwera: `X.X.X.X`
   - Login: `root` lub `mikrus`
   - Hasło: `xxxxxxxxx`

---

### **KROK 2: Konfiguracja serwera**

```bash
# 1. Połącz się przez SSH
ssh root@X.X.X.X

# 2. Update systemu
apt update && apt upgrade -y

# 3. Zainstaluj Docker
curl -fsSL https://get.docker.com -o get-docker.sh
sh get-docker.sh

# 4. Zainstaluj Docker Compose
apt install docker-compose -y

# 5. Sprawdź wersje
docker --version
docker-compose --version

# 6. Utwórz użytkownika dla aplikacji (opcjonalnie)
adduser sportadmin
usermod -aG docker sportadmin
su - sportadmin
```

---

### **KROK 3: Upload projektu**

```bash
# Na TWOIM komputerze (lokalnie):

# 1. Utwórz .env file z sekretami
cat > .env << EOF
DB_PASSWORD=TwojeHasloDoPostgreSQL123!
STRIPE_SECRET_KEY=sk_test_...
STRIPE_PUBLISHABLE_KEY=pk_test_...
STRIPE_WEBHOOK_SECRET=whsec_...
JWT_SECRET=$(openssl rand -base64 32)
EMAIL_HOST=smtp.op.pl
EMAIL_PORT=587
EMAIL_USERNAME=contact.sportrental@op.pl
EMAIL_PASSWORD=TwojeHasloEmail
EMAIL_FROM=contact.sportrental@op.pl
EOF

# 2. Wyślij projekt na serwer
rsync -avz --exclude='bin' --exclude='obj' --exclude='*.db' \
  ./ root@X.X.X.X:/opt/sportrental/

# Lub użyj git:
# Na serwerze:
cd /opt
git clone https://github.com/DamianTarnowski/SportRental.git sportrental
cd sportrental
nano .env  # Wklej sekrety
```

---

### **KROK 4: Build i uruchom**

```bash
# NA SERWERZE:
cd /opt/sportrental

# 1. Zbuduj obrazy (zajmie 5-10 min)
docker-compose build

# 2. Uruchom kontenery
docker-compose up -d

# 3. Sprawdź status
docker-compose ps

# Powinno pokazać:
# sportrental-nginx    nginx:alpine   Up   0.0.0.0:80->80/tcp, 0.0.0.0:443->443/tcp
# sportrental-admin    ...            Up   5001:8080/tcp
# sportrental-api      ...            Up   5002:8080/tcp
# sportrental-media    ...            Up   5003:8080/tcp
# sportrental-client   ...            Up   5004:80/tcp
# sportrental-db       postgres:14    Up   5432/tcp

# 4. Sprawdź logi
docker-compose logs -f

# 5. Uruchom migracje EF Core
docker exec -it sportrental-admin dotnet ef database update

# Lub ręcznie:
docker exec -it sportrental-db psql -U sportadmin -d sportrental -c "SELECT version();"
```

---

### **KROK 5: Konfiguracja domeny i SSL**

#### **Opcja A: Darmowa domena + SSL (Certbot)**

```bash
# 1. Zainstaluj Certbot
apt install certbot python3-certbot-nginx -y

# 2. Skonfiguruj domenę (musisz mieć domenę wskazującą na IP serwera)
# Dodaj rekord A w DNS:
#   @ -> X.X.X.X (twój IP)
#   www -> X.X.X.X

# 3. Wygeneruj certyfikat
certbot --nginx -d twoja-domena.pl -d www.twoja-domena.pl

# 4. Automatyczne odnowienie
certbot renew --dry-run
```

#### **Opcja B: Cloudflare Tunnel (bez domeny)**

```bash
# Jeśli nie masz domeny, użyj Cloudflare Tunnel (darmowe)
# 1. Zainstaluj cloudflared
wget https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-amd64
chmod +x cloudflared-linux-amd64
mv cloudflared-linux-amd64 /usr/local/bin/cloudflared

# 2. Zaloguj się
cloudflared tunnel login

# 3. Utwórz tunnel
cloudflared tunnel create sportrental

# 4. Skonfiguruj routing
cloudflared tunnel route dns sportrental twoja-subdomena.your-domain.com

# 5. Uruchom tunnel
cloudflared tunnel run sportrental
```

---

## 📊 **Monitorowanie**

### **Sprawdź zużycie zasobów:**

```bash
# Zużycie RAM i CPU wszystkich kontenerów
docker stats

# Zużycie dysku
df -h
docker system df

# Logi z ostatnich 100 linii
docker-compose logs --tail=100

# Live logs dla konkretnego serwisu
docker-compose logs -f admin
docker-compose logs -f api

# Restart kontenera
docker-compose restart admin

# Stop wszystkiego
docker-compose down

# Start
docker-compose up -d
```

### **Monitoring z Portainer (opcjonalnie):**

```bash
# Zainstaluj Portainer (Docker GUI)
docker volume create portainer_data
docker run -d -p 9000:9000 \
  --name portainer --restart=always \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -v portainer_data:/data \
  portainer/portainer-ce

# Otwórz http://X.X.X.X:9000
```

---

## 💾 **Backup**

### **Automatyczny backup bazy danych:**

```bash
# Utwórz skrypt backup
cat > /opt/backup-db.sh << 'EOF'
#!/bin/bash
DATE=$(date +%Y%m%d_%H%M%S)
BACKUP_DIR="/opt/backups"
mkdir -p $BACKUP_DIR

# Backup PostgreSQL
docker exec sportrental-db pg_dump -U sportadmin sportrental | gzip > $BACKUP_DIR/db_$DATE.sql.gz

# Backup media files
tar -czf $BACKUP_DIR/media_$DATE.tar.gz /opt/sportrental/media_data

# Usuń backupy starsze niż 7 dni
find $BACKUP_DIR -type f -mtime +7 -delete

echo "Backup completed: $DATE"
EOF

chmod +x /opt/backup-db.sh

# Dodaj do cron (codziennie o 3:00)
crontab -e
# Dodaj linię:
0 3 * * * /opt/backup-db.sh >> /var/log/backup.log 2>&1
```

### **Restore z backupu:**

```bash
# Restore bazy danych
gunzip < /opt/backups/db_20250108_030000.sql.gz | \
  docker exec -i sportrental-db psql -U sportadmin sportrental

# Restore plików
tar -xzf /opt/backups/media_20250108_030000.tar.gz -C /
```

---

## 🔒 **Zabezpieczenia**

```bash
# 1. Firewall (ufw)
apt install ufw -y
ufw default deny incoming
ufw default allow outgoing
ufw allow ssh
ufw allow 80/tcp
ufw allow 443/tcp
ufw enable

# 2. Fail2ban (ochrona przed brute-force)
apt install fail2ban -y
systemctl enable fail2ban
systemctl start fail2ban

# 3. Automatyczne aktualizacje
apt install unattended-upgrades -y
dpkg-reconfigure -plow unattended-upgrades

# 4. Zmień domyślny port SSH (opcjonalnie)
nano /etc/ssh/sshd_config
# Zmień: Port 22 -> Port 2222
systemctl restart sshd
ufw allow 2222/tcp
```

---

## 💰 **Porównanie kosztów: mikr.us vs Azure**

| Aspekt | **mikr.us** | **Azure** |
|--------|-------------|-----------|
| **RAM** | 4 GB | 4 GB (B1s × 4) |
| **CPU** | Shared | 1 vCore × 4 |
| **Dysk** | 40 GB SSD | 10 GB × 4 |
| **Transfer** | Nielimitowany | 10 GB/app |
| **Cena/miesiąc** | **~16 zł** | **~200 zł** |
| **Cena/rok** | **~197 zł** | **~2400 zł** |
| **Lokalizacja** | Polska | West Europe |
| **Latencja (PL)** | < 10ms | ~30ms |
| **Skalowanie** | Ręczne | Automatyczne |
| **Backup** | Ręczne | Automatyczne |
| **Monitoring** | Docker stats | Application Insights |
| **SSL** | Let's Encrypt | Azure-managed |

### **🏆 WERDYKT:**

| Dla kogo | Polecam |
|----------|---------|
| **MVP / Prototyp** | 🐋 mikr.us (oszczędność ~2200 zł/rok) |
| **Mały biznes (< 100 users)** | 🐋 mikr.us |
| **Średni biznes (100-1000 users)** | ⚖️ Azure lub upgrade mikr.us |
| **Duży biznes (> 1000 users)** | ☁️ Azure (auto-scaling) |
| **Startupy z fundusze** | ☁️ Azure (infrastruktura managed) |

---

## ✅ **Checklist Po Deployment**

- [ ] ✅ VPS zakupiony i skonfigurowany
- [ ] ✅ Docker i Docker Compose zainstalowane
- [ ] ✅ Projekt wgrany na serwer
- [ ] ✅ `.env` z sekretami utworzony
- [ ] ✅ Kontenery uruchomione (`docker-compose up -d`)
- [ ] ✅ Migracje EF Core wykonane
- [ ] ✅ Seed data załadowane
- [ ] ✅ Nginx reverse proxy skonfigurowany
- [ ] ✅ Domena skonfigurowana (DNS)
- [ ] ✅ SSL certyfikat wygenerowany (Let's Encrypt)
- [ ] ✅ Firewall (ufw) włączony
- [ ] ✅ Fail2ban skonfigurowany
- [ ] ✅ Automatyczne backupy ustawione
- [ ] ✅ Monitoring działający
- [ ] ✅ Aplikacja dostępna przez HTTPS

---

## 🆘 **Troubleshooting**

### **Problem: Out of Memory (OOM)**

```bash
# Sprawdź zużycie
docker stats

# Jeśli przekracza 3.5GB:
# 1. Wyłącz Admin Panel gdy nie używany
docker-compose stop admin

# 2. Zmniejsz limity pamięci w docker-compose.yml
# 3. Dodaj swap (2GB)
fallocate -l 2G /swapfile
chmod 600 /swapfile
mkswap /swapfile
swapon /swapfile
echo '/swapfile none swap sw 0 0' >> /etc/fstab
```

### **Problem: Kontenery nie startują**

```bash
# Sprawdź logi
docker-compose logs

# Restart wszystkiego
docker-compose down
docker-compose up -d

# Rebuild z czystego stanu
docker-compose down -v
docker system prune -a
docker-compose build --no-cache
docker-compose up -d
```

---

## 📚 **Dodatkowe zasoby**

- [mikr.us oficjalna strona](https://mikr.us)
- [Docker Docs](https://docs.docker.com/)
- [Nginx Docs](https://nginx.org/en/docs/)
- [Let's Encrypt Certbot](https://certbot.eff.org/)

---

## 📧 **Kontakt**

**Email:** hdtdtr@gmail.com  
**GitHub:** https://github.com/DamianTarnowski/SportRental

---

**Powodzenia z deploymentem na mikr.us! 🚀**



