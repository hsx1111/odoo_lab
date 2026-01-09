using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace WebApp.Controllers
{

    public class OrderLineViewModel
    {
        public string ProductName { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Subtotal { get; set; }
    }

    public class OrderViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string State { get; set; } = "";
        public string DateOrder { get; set; } = "";
        public decimal Total { get; set; }
        public List<OrderLineViewModel> Lines { get; set; } = new();
    }

    // Petit modèle pour représenter un produit dans la vue
    public class ProductViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string DefaultCode { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public decimal ListPrice { get; set; }
        public decimal QtyAvailable { get; set; }

        // Champs locatifs (extension 6.4)
        public int MaxGuests { get; set; }
        public int Beds { get; set; }
        public int Bedrooms { get; set; }
        public int Bathrooms { get; set; }
        public bool PoolAvailable { get; set; }
        public bool AirConditioningAvailable { get; set; }
    }

    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        private async Task<OrderViewModel> GetOrderAsync(string baseUrl, string sessionId, int orderId)
        {
            // --- Order header
            var orderDomain = new object[]
            {
                new object[] { "id", "=", orderId }
            };

            var orderArgs = new object[] { orderDomain };

            var orderKwargs = new
            {
                fields = new[] { "id", "name", "partner_id", "date_order", "state", "amount_total" },
                limit = 1
            };

            var orderResult = await CallOdooAsync(baseUrl, sessionId, "sale.order", "search_read", orderArgs, orderKwargs);

            if (orderResult.ValueKind != JsonValueKind.Array || orderResult.GetArrayLength() == 0)
                throw new Exception($"Commande {orderId} introuvable.");

            var o = orderResult[0];

            var vm = new OrderViewModel
            {
                Id = orderId,
                Name = o.TryGetProperty("name", out var nameEl) ? SafeString(nameEl) : "",
                State = o.TryGetProperty("state", out var stateEl) ? SafeString(stateEl) : "",
                DateOrder = o.TryGetProperty("date_order", out var dateEl) ? SafeString(dateEl) : ""
            };

            if (o.TryGetProperty("amount_total", out var totalEl) && totalEl.TryGetDecimal(out var total))
                vm.Total = total;

            // partner_id is [id, "Name"] or false
            if (o.TryGetProperty("partner_id", out var partnerEl) &&
                partnerEl.ValueKind == JsonValueKind.Array &&
                partnerEl.GetArrayLength() >= 2)
            {
                vm.CustomerName = SafeString(partnerEl[1]);
            }

            // --- Lines
            var lineDomain = new object[]
            {
                new object[] { "order_id", "=", orderId }
            };

            var lineArgs = new object[] { lineDomain };

            var lineKwargs = new
            {
                fields = new[] { "product_id", "product_uom_qty", "price_unit", "price_subtotal" },
                limit = 200
            };

            var lineResult = await CallOdooAsync(baseUrl, sessionId, "sale.order.line", "search_read", lineArgs, lineKwargs);

            if (lineResult.ValueKind == JsonValueKind.Array)
            {
                foreach (var line in lineResult.EnumerateArray())
                {
                    var lvm = new OrderLineViewModel();

                    // product_id: [id, "Name"] or false
                    if (line.TryGetProperty("product_id", out var prodEl) &&
                        prodEl.ValueKind == JsonValueKind.Array &&
                        prodEl.GetArrayLength() >= 2)
                    {
                        lvm.ProductName = SafeString(prodEl[1]);
                    }

                    if (line.TryGetProperty("product_uom_qty", out var qtyEl) && qtyEl.TryGetDecimal(out var qty))
                        lvm.Quantity = qty;

                    if (line.TryGetProperty("price_unit", out var puEl) && puEl.TryGetDecimal(out var pu))
                        lvm.UnitPrice = pu;

                    if (line.TryGetProperty("price_subtotal", out var subEl) && subEl.TryGetDecimal(out var sub))
                        lvm.Subtotal = sub;

                    vm.Lines.Add(lvm);
                }
            }

            return vm;
        }

        private static string SafeString(JsonElement el)
        {
            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString() ?? "",
                JsonValueKind.False => "",          // Odoo renvoie false pour "vide"
                JsonValueKind.Null => "",
                _ => el.ToString()                  // fallback
            };
        }

        public IActionResult Index()
        {
            // Valeurs par défaut dans le formulaire
            ViewBag.OdooUrl = "http://localhost:8069";
            ViewBag.OdooDb = "odoo_lab";
            ViewBag.OdooLogin = "admin";
            ViewBag.OdooPassword = "admin";
            return View();
        }

        /// <summary>
        /// Action appelée quand on soumet le formulaire "Connexion / Charger les produits"
        /// </summary>
        
        [HttpPost]
        public async Task<IActionResult> CreateOrder(
            string odooUrl,
            string odooDb,
            string odooLogin,
            string odooPassword,
            int productId,
            int quantity)
        {
            _logger.LogInformation("CreateOrder received productId={ProductId}, quantity={Quantity}", productId, quantity);

            // On renvoie les valeurs au formulaire
            ViewBag.OdooUrl = odooUrl;
            ViewBag.OdooDb = odooDb;
            ViewBag.OdooLogin = odooLogin;
            ViewBag.OdooPassword = odooPassword;

            try
            {
                // 1) Auth
                var auth = await AuthenticateOdooAsync(odooUrl, odooDb, odooLogin, odooPassword);

                // 2) Trouver un client (partner) -> Administrator
                var partnerId = await GetPartnerIdAsync(odooUrl, auth.SessionId);

                // 3) Créer sale.order
                var orderId = await CreateSaleOrderAsync(odooUrl, auth.SessionId, partnerId);

                // 4) Créer sale.order.line avec le produit choisi
                await CreateSaleOrderLineAsync(odooUrl, auth.SessionId, orderId, productId, quantity);

                // 5) Recharger les produits pour réafficher la page
                var products = await GetProductsAsync(odooUrl, auth.SessionId);

                ViewBag.Success = true;
                ViewBag.Message = $"✅ Commande créée avec succès (order_id = {orderId}) pour product_id = {productId}.";
                ViewBag.Products = products;
                // on stocke l'id pour afficher le bouton suivre
                ViewBag.CreatedOrderId = orderId;

                return View("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la création de commande");
                ViewBag.Success = false;
                ViewBag.Message = "Erreur: " + ex.Message;
                ViewBag.Products = null;
                ViewBag.CreatedOrderId = null;
                return View("Index");
            }
        }

        [HttpPost]
        public async Task<IActionResult> Connect(
            string odooUrl,
            string odooDb,
            string odooLogin,
            string odooPassword)
        {
            // On renvoie les valeurs au formulaire en cas d'erreur
            ViewBag.OdooUrl = odooUrl;
            ViewBag.OdooDb = odooDb;
            ViewBag.OdooLogin = odooLogin;
            ViewBag.OdooPassword = odooPassword;

            try
            {
                if (string.IsNullOrWhiteSpace(odooUrl) ||
                    string.IsNullOrWhiteSpace(odooDb) ||
                    string.IsNullOrWhiteSpace(odooLogin) ||
                    string.IsNullOrWhiteSpace(odooPassword))
                {
                    throw new Exception("Merci de remplir tous les champs de configuration.");
                }

                // 1) Authentification
                var authResult = await AuthenticateOdooAsync(odooUrl, odooDb, odooLogin, odooPassword);

                // 2) Récupération des produits via search_read
                var products = await GetProductsAsync(odooUrl, authResult.SessionId);

                ViewBag.Success = true;
                ViewBag.Message = $"Connexion réussie (uid = {authResult.Uid}). " +
                                  $"{products.Count} produit(s) chargé(s).";
                ViewBag.Products = products;

                return View("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la connexion à Odoo");
                ViewBag.Success = false;
                ViewBag.Message = ex.Message;
                ViewBag.Products = null;
                return View("Index");
            }
        }

        [HttpGet]
        public async Task<IActionResult> Order(
            string odooUrl,
            string odooDb,
            string odooLogin,
            string odooPassword,
            int orderId)
        {
            ViewBag.OdooUrl = odooUrl;
            ViewBag.OdooDb = odooDb;
            ViewBag.OdooLogin = odooLogin;
            ViewBag.OdooPassword = odooPassword;

            try
            {
                var auth = await AuthenticateOdooAsync(odooUrl, odooDb, odooLogin, odooPassword);

                var order = await GetOrderAsync(odooUrl, auth.SessionId, orderId);
                return View(order); // View: Views/Home/Order.cshtml
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur suivi commande");
                ViewBag.Success = false;
                ViewBag.Message = "Erreur: " + ex.Message;
                return View("Index");
            }
        }

        // ------------------------
        //  Authentification Odoo
        // ------------------------

        private record OdooAuthResult(int Uid, string SessionId);

        /// <summary>
        /// Appelle /web/session/authenticate en JSON-RPC
        /// </summary>
        private async Task<OdooAuthResult> AuthenticateOdooAsync(
            string baseUrl,
            string db,
            string login,
            string password)
        {
            var cookieContainer = new CookieContainer();
            var handler = new HttpClientHandler
            {
                UseCookies = true,
                CookieContainer = cookieContainer
            };

            using var client = new HttpClient(handler);

            var payload = new
            {
                jsonrpc = "2.0",
                method = "call",
                // "params" est un mot réservé en C#, on écrit "@params"
                @params = new
                {
                    db = db,
                    login = login,
                    password = password
                },
                id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = null // garde les noms tels quels
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{baseUrl.TrimEnd('/')}/web/session/authenticate", content);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Erreur HTTP (authentification) : {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var body = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(body);

            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorElement))
            {
                var message = errorElement.GetProperty("data").GetProperty("message").GetString();
                throw new Exception($"Erreur JSON-RPC lors de l'authentification : {message}");
            }

            var result = root.GetProperty("result");

            var uid = result.GetProperty("uid").GetInt32();
            // Odoo 18: session_id est renvoyé via Set-Cookie, pas dans le JSON
            var cookies = cookieContainer.GetCookies(new Uri(baseUrl.TrimEnd('/')));
            var sessionId = cookies["session_id"]?.Value
                            ?? throw new Exception("Cookie session_id introuvable après authentification.");

            return new OdooAuthResult(uid, sessionId);
        }

        // ------------------------
        //  Appel générique call_kw
        // ------------------------

        /// <summary>
        /// Méthode générique pour appeler /web/dataset/call_kw
        /// </summary>
        private async Task<JsonElement> CallOdooAsync(
            string baseUrl,
            string sessionId,
            string model,
            string method,
            object[] args,
            object kwargs)
        {
            using var client = new HttpClient();

            var payload = new
            {
                jsonrpc = "2.0",
                method = "call",
                @params = new
                {
                    model = model,
                    method = method,
                    args = args,
                    kwargs = kwargs
                },
                id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = null
            });


            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{baseUrl.TrimEnd('/')}/web/dataset/call_kw")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            // ✅ Le plus fiable : envoyer la session comme un vrai navigateur (cookie)
            request.Headers.Remove("Cookie");
            request.Headers.Add("Cookie", $"session_id={sessionId}");

            // ✅ Optionnel : tu peux garder aussi ce header
            request.Headers.Remove("X-Openerp-Session-Id");
            request.Headers.Add("X-Openerp-Session-Id", sessionId);

            // 🚀 Maintenant on envoie
            var response = await client.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Erreur HTTP (call_kw) : {(int)response.StatusCode} {response.ReasonPhrase} - {responseBody}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorElement))
            {
                var message = errorElement.GetProperty("data").GetProperty("message").GetString();
                throw new Exception($"Erreur JSON-RPC : {message}");
            }

            return root.GetProperty("result").Clone();
        }

        // ------------------------
        //  Récupération des produits
        // ------------------------

        private async Task<List<ProductViewModel>> GetProductsAsync(string baseUrl, string sessionId)
        {
            // Champs à récupérer (incluant l'extension "rental property")
            var fields = new[]
            {
                "id",
                "name",
                "list_price",
                "type",
                "default_code",
                "categ_id",
                "qty_available"
            };

            // Filtre : uniquement les propriétés locatives (max_guests > 0)
            // Si tu veux tous les produits, mets simplement new object[] { }
            var domain = new List<object>();

            var args = new object[]
            {
                domain
            };

            var kwargs = new
            {
                fields = fields,
                limit = 50
            };

            var result = await CallOdooAsync(
                baseUrl,
                sessionId,
                "product.template",
                "search_read",
                args,
                kwargs);

            var list = new List<ProductViewModel>();

            if (result.ValueKind != JsonValueKind.Array)
            {
                return list;
            }

            foreach (var item in result.EnumerateArray())
            {
                var p = new ProductViewModel();

                if (item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
                {
                    p.Id = idEl.GetInt32();
                }

                p.Name = item.TryGetProperty("name", out var nameEl)
                    ? SafeString(nameEl)
                    : "";

                p.Type = item.TryGetProperty("type", out var typeEl)
                    ? SafeString(typeEl)
                    : "";

                p.DefaultCode = item.TryGetProperty("default_code", out var codeEl)
                    ? SafeString(codeEl)
                    : "";

                // list_price
                if (item.TryGetProperty("list_price", out var priceEl) &&
                    priceEl.ValueKind is JsonValueKind.Number &&
                    priceEl.TryGetDecimal(out var price))
                {
                    p.ListPrice = price;
                }

                // qty_available
                if (item.TryGetProperty("qty_available", out var qtyEl) &&
                    qtyEl.ValueKind is JsonValueKind.Number &&
                    qtyEl.TryGetDecimal(out var qty))
                {
                    p.QtyAvailable = qty;
                }

                // categ_id est du type [id, "Nom de la catégorie"]
                if (item.TryGetProperty("categ_id", out var categEl))
                {
                    if (categEl.ValueKind == JsonValueKind.Array && categEl.GetArrayLength() >= 2)
                    {
                        p.CategoryName = SafeString(categEl[1]);
                    }
                    else
                    {
                        // false ou null => pas de catégorie
                        p.CategoryName = "";
                    }
                }

                // Champs locatifs
                if (item.TryGetProperty("max_guests", out var mgEl) &&
                    mgEl.ValueKind == JsonValueKind.Number)
                {
                    p.MaxGuests = mgEl.GetInt32();
                }

                if (item.TryGetProperty("beds", out var bedsEl) &&
                    bedsEl.ValueKind == JsonValueKind.Number)
                {
                    p.Beds = bedsEl.GetInt32();
                }

                if (item.TryGetProperty("bedrooms", out var bedRoomsEl) &&
                    bedRoomsEl.ValueKind == JsonValueKind.Number)
                {
                    p.Bedrooms = bedRoomsEl.GetInt32();
                }

                if (item.TryGetProperty("bathrooms", out var bathsEl) &&
                    bathsEl.ValueKind == JsonValueKind.Number)
                {
                    p.Bathrooms = bathsEl.GetInt32();
                }

                if (item.TryGetProperty("pool_available", out var poolEl) &&
                    poolEl.ValueKind == JsonValueKind.True)
                {
                    p.PoolAvailable = true;
                }

                if (item.TryGetProperty("air_conditioning_available", out var acEl) &&
                    acEl.ValueKind == JsonValueKind.True)
                {
                    p.AirConditioningAvailable = true;
                }

                list.Add(p);
            }

            return list;
        }
        private async Task<int> GetPartnerIdAsync(string baseUrl, string sessionId)
        {
            // Cherche "Administrator" dans res.partner
            var domain = new object[]
            {
                new object[] { "name", "=", "Administrator" }
            };

            var args = new object[] { domain };

            var kwargs = new
            {
                fields = new[] { "id", "name" },
                limit = 1
            };

            var result = await CallOdooAsync(baseUrl, sessionId, "res.partner", "search_read", args, kwargs);

            if (result.ValueKind == JsonValueKind.Array && result.GetArrayLength() > 0)
            {
                var first = result[0];
                if (first.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
                    return idEl.GetInt32();
            }

            throw new Exception("Impossible de trouver le client 'Administrator' (res.partner).");
        }

        private async Task<int> CreateSaleOrderAsync(string baseUrl, string sessionId, int partnerId)
        {
            var values = new
            {
                partner_id = partnerId
            };

            // create attend args = [ {values} ]
            var args = new object[] { values };

            var result = await CallOdooAsync(baseUrl, sessionId, "sale.order", "create", args, new { });

            // Odoo renvoie l'ID créé (nombre)
            if (result.ValueKind == JsonValueKind.Number)
                return result.GetInt32();

            throw new Exception("Création sale.order: retour inattendu.");
        }

        private async Task<int> CreateSaleOrderLineAsync(string baseUrl, string sessionId, int orderId, int productId, int quantity)
        {
            var values = new
            {
                order_id = orderId,
                product_id = productId,
                product_uom_qty = quantity
            };

            var args = new object[] { values };

            var result = await CallOdooAsync(baseUrl, sessionId, "sale.order.line", "create", args, new { });

            if (result.ValueKind == JsonValueKind.Number)
                return result.GetInt32();

            throw new Exception("Création sale.order.line: retour inattendu.");
        }
    }
}
