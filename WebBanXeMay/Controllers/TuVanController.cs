using Microsoft.AspNetCore.Mvc;
using WebBanXeMay.Data;
using WebBanXeMay.Models;

namespace WebBanXeMay.Controllers
{
    public class TuVanController : Controller
    {
        private readonly AppDbContext _context;

        public TuVanController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(TuVan model)
        {
            if (ModelState.IsValid)
            {
                _context.TuVans.Add(model);
                _context.SaveChanges();

                // ✅ Lưu message
                TempData["SuccessMessage"] = "Gửi thông tin tư vấn thành công! Chúng tôi sẽ liên hệ với bạn sớm nhất.";

                // ✅ Chuyển sang trang popup riêng
                return RedirectToAction("Success");
            }

            return View(model);
        }

        // ✅ Trang popup hiển thị thông báo
        public IActionResult Success()
        {
            return View();
        }
    }
}
