# 🖼️ Media & Image Features

> Complete guide to image management, optimization, and display features in SportRental

## 📋 Overview

SportRental includes a sophisticated image management system with:

- **✅ Image Cropper** - Interactive image editing with Croppie.js
- **✅ Multiple Resolutions** - Automatic thumbnail generation (w400, w800, w1280)
- **✅ Lightbox** - Full-screen image viewer
- **✅ Azure Blob Storage** - Cloud storage with CDN-ready URLs
- **✅ Validation** - Client-side file size and type checking
- **✅ Responsive Images** - srcset and sizes attributes for optimal loading

---

## 🎨 Features

### 1. Image Cropper (Admin Panel)

**Interactive image editing** before upload:

- **Validation:** 8MB max, JPEG/PNG/WebP only
- **Crop:** Square viewport (300x300px)
- **Zoom:** Ctrl + scroll or slider
- **Rotate:** 90° left/right
- **Preview:** Real-time updates
- **Quality:** 85% (optimal compression)
- **Max output:** 1920x1920px

**User Options:**
- **Option A:** Crop & edit before uploading
- **Option B:** Skip crop - quick upload of original file

---

### 2. Automatic Thumbnails

**Backend automatically generates 3 resolutions** when you upload an image:

| Size | Dimensions | Use Case | File Size |
|------|-----------|----------|-----------|
| **w400** | 400px width | Product grid, mobile | ~100KB |
| **w800** | 800px width | Desktop grid, details (tablet) | ~250KB |
| **w1280** | 1280px width | Product details (desktop) | ~500KB |
| **original** | Full resolution | Lightbox, download | 2-10MB |

All images are saved to **Azure Blob Storage** with CDN-ready URLs.

---

### 3. Responsive Display

**Intelligent image loading** based on screen size:

#### **Product Grid (4 columns):**
```html
<img srcset="w400.jpg 400w, w800.jpg 800w, w1280.jpg 1280w"
     sizes="(max-width: 600px) 400px, (max-width: 900px) 400px, 800px"
     src="w400.jpg" />
```

**Result:**
- Mobile (≤600px): `w400.jpg` - very fast!
- Tablet (600-900px): `w400.jpg` - still fast!
- Desktop (>900px): `w800.jpg` - high quality

---

#### **Product Details:**
```html
<img srcset="w400.jpg 400w, w800.jpg 800w, w1280.jpg 1280w"
     sizes="(max-width: 600px) 400px, (max-width: 900px) 800px, 1280px"
     src="w1280.jpg" />
```

**Result:**
- Mobile: `w400.jpg` (~100KB)
- Tablet: `w800.jpg` (~250KB)
- Desktop: `w1280.jpg` (~500KB) - crystal clear!

---

#### **Lightbox (Full Screen):**
```html
<img src="original.jpg" />
```

**Result:**
- Always `original.jpg` - maximum quality
- Full resolution for zoom and download
- Professional product presentation

---

### 4. Lightbox Feature

**Click to enlarge** any product image:

**Features:**
- ✅ Full-screen modal (MudDialog)
- ✅ Original quality image
- ✅ Download button
- ✅ Keyboard shortcuts (ESC to close)
- ✅ Responsive (mobile & desktop)
- ✅ Visual hint: "Kliknij aby powiększyć" badge

**UX:**
```
Product Details → Click image → Lightbox opens → Full screen view
```

---

## 🚀 How to Use

### Admin Panel - Adding Product with Image

1. **Click "Dodaj produkt"**
2. **Fill product data** (name, SKU, price, etc.)
3. **In "Zdjęcie produktu" section:**
   
   **Option A - Upload with cropper:**
   - Click "Wybierz plik"
   - Select image (max 8MB, JPEG/PNG/WebP)
   - Crop/rotate using controls:
     - **Zoom:** Ctrl + scroll or slider
     - **Rotate:** Rotation icons (90° left/right)
   - Click **"Przytnij i zapisz"**
   
   **Option B - Quick upload (skip crop):**
   - Click "Wybierz plik"
   - Select image
   - Click **"Pomiń obcinanie"**
   - Image uploaded without editing

4. **Click "Zapisz"** - image processed and saved to Azure Blob Storage with 3 resolutions

---

### Client UI - Viewing Product Images

**Product Grid:**
- Small, optimized thumbnails
- Fast loading
- Click on product to view details

**Product Details:**
- Large, high-quality image
- Click to open lightbox

**Lightbox:**
- Full-screen view
- Original resolution
- Download option
- ESC or X to close

---

## 🛠️ Technical Details

### Frontend (Blazor Server)

**Component: `ImageCropper.razor`**

```razor
<ImageCropper MaxSizeMB="8" 
              OnImageReady="OnImageReadySkipCrop"
              OnBase64ImageReady="OnImageReadyCropped" />
```

**Parameters:**
- `MaxSizeMB` - Max file size (default 8MB)
- `OnImageReady` - Event for "skip crop" (returns IBrowserFile)
- `OnBase64ImageReady` - Event after crop (returns base64 string)

---

**JavaScript API (`wwwroot/js/image-cropper.js`):**

```javascript
// Validate file
const result = imageCropper.validateImage(file, maxSizeMB);

// Initialize cropper
imageCropper.init(imageDataUrl, containerId);

// Get cropped image
const croppedBase64 = await imageCropper.getCroppedImage('jpeg', 0.85);

// Rotate image
imageCropper.rotate(90); // or -90

// Destroy
imageCropper.destroy();
```

---

**Helper Method: `GetImageUrl()`**

```csharp
public static class ProductExtensions
{
    public static string GetImageUrl(this Product product, int width)
    {
        if (string.IsNullOrEmpty(product.ImageUrl)) 
            return "/images/placeholder.jpg";
        
        var baseUrl = product.ImageUrl.Replace("/original.", $"/w{width}.");
        return baseUrl;
    }
    
    public static string GetOriginalImageUrl(this Product product)
    {
        return product.ImageUrl ?? "/images/placeholder.jpg";
    }
}
```

---

### Backend (API)

**Endpoint:** `POST /api/products/{id}/image`

**Process:**
1. **Receive** multipart/form-data file upload
2. **Validate** file size and type
3. **Generate** 3 thumbnails (w400, w800, w1280) using ImageSharp
4. **Upload** all 4 images to Azure Blob Storage
5. **Update** product record with `original.jpg` URL
6. **Return** URLs to client

**Technologies:**
- **SixLabors.ImageSharp** - Image processing
- **Azure.Storage.Blobs** - Cloud storage
- **ASP.NET Core** - Minimal APIs

---

### SignalR Configuration

**Increased message size** for large file uploads:

```csharp
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10MB
});
```

**Why?**
- Default SignalR limit: 32KB
- Image cropper sends base64 data via SignalR
- 8MB image → ~11MB base64 → needs 10MB+ limit

---

## 📊 Performance Benefits

| Context | Before | After | Improvement |
|---------|--------|-------|-------------|
| Product grid load | 10MB (4 originals) | 0.4MB (4×w400) | **96% faster!** |
| Mobile detail view | 5MB original | 100KB w400 | **98% faster!** |
| Desktop detail view | 5MB original | 500KB w1280 | **90% faster!** |
| Lightbox (optional) | n/a | Original (on demand) | No compromise! |

---

## 🎯 Best Practices

### For Administrators:

1. **Use high-quality source images** (min 1920x1920px recommended)
2. **Crop carefully** - thumbnails are generated from cropped version
3. **Skip crop** only if image is already perfectly framed
4. **Monitor storage** - 4 files per image (original + 3 thumbnails)

### For Developers:

1. **Always use srcset** for responsive images
2. **Set appropriate sizes** attribute for each context
3. **Lazy load** images below the fold
4. **Test on slow 3G** to verify performance
5. **Monitor Azure Blob Storage** costs

---

## 🐛 Troubleshooting

### Image Cropper Issues

**❌ "File too large"**
- **Fix:** Ensure file < 8MB
- **Fix:** Compress image before upload

**❌ "Invalid file type"**
- **Fix:** Use JPEG, PNG, or WebP only
- **Fix:** Check file extension

**❌ "Cropper not loading"**
- **Fix:** Check browser console for JS errors
- **Fix:** Verify `croppie.min.js` is loaded

---

### Display Issues

**❌ "Images not showing in grid"**
- **Fix:** Check Azure Blob Storage connection
- **Fix:** Verify `ImageUrl` in database contains correct URL

**❌ "Lightbox not opening"**
- **Fix:** Check if `GetOriginalImageUrl()` returns valid URL
- **Fix:** Verify MudDialog is configured

**❌ "Slow loading on mobile"**
- **Fix:** Verify `srcset` and `sizes` attributes are set
- **Fix:** Check if w400 thumbnails exist in blob storage

---

### Backend Issues

**❌ "Upload fails"**
- **Fix:** Check Azure Blob Storage connection string in Key Vault
- **Fix:** Verify SignalR MaximumReceiveMessageSize is 10MB+

**❌ "Thumbnails not generated"**
- **Fix:** Verify ImageSharp package is installed
- **Fix:** Check API logs for errors

---

## 📚 Related Documentation

- [SECURITY.md](../SECURITY.md) - Azure Blob Storage configuration
- [setup/AZURE_BLOB_STORAGE_SETUP.md](setup/AZURE_BLOB_STORAGE_SETUP.md) - Storage setup guide
- [ADMIN_PANEL_COMPANY_INFO.md](guides/ADMIN_PANEL_COMPANY_INFO.md) - Admin panel features
- [ARCHITECTURE.md](ARCHITECTURE.md) - System architecture

---

## 🎉 Summary

**What You Get:**

✅ **Interactive Image Cropper** - Professional editing before upload  
✅ **Automatic Thumbnails** - 3 sizes generated automatically  
✅ **Responsive Images** - Optimal size for each device  
✅ **Lightbox** - Full-screen high-quality view  
✅ **Azure Blob Storage** - Scalable cloud storage  
✅ **Fast Loading** - 90-98% reduction in transfer size  

**Ready For:**

🎨 **Professional Product Photography** - High-quality presentation  
📱 **Mobile-First Design** - Fast loading on slow connections  
💰 **Cost Optimization** - Reduced bandwidth usage  
⚡ **Performance** - Instant page loads  

---

**Last updated:** 2025-10-07  
**Status:** ✅ Production Ready  
**Technologies:** Croppie.js, ImageSharp, Azure Blob Storage, SignalR
