using CarCareTracker.Helper;
using CarCareTracker.Logic;
using CarCareTracker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarCareTracker.Controllers
{
    [Authorize(Roles = nameof(UserData.IsAdmin))]
    public class AdminController : Controller
    {
        private ILoginLogic _loginLogic;
        private IUserLogic _userLogic;
        private IConfigHelper _configHelper;
        private ITranslationHelper _translator;
        public AdminController(ILoginLogic loginLogic, IUserLogic userLogic, IConfigHelper configHelper, ITranslationHelper translator)
        {
            _loginLogic = loginLogic;
            _userLogic = userLogic;
            _configHelper = configHelper;
            _translator = translator;
        }
        public IActionResult Index()
        {
            var viewModel = new AdminViewModel
            {
                Users = _loginLogic.GetAllUsers().OrderBy(x=>x.Id).ToList(),
                Tokens = _loginLogic.GetAllTokens()
            };
            return View(viewModel);
        }
        public IActionResult GetTokenPartialView()
        {
            var viewModel = _loginLogic.GetAllTokens();
            return PartialView("_Tokens", viewModel);
        }
        public IActionResult GetUserPartialView()
        {
            var viewModel = _loginLogic.GetAllUsers().OrderBy(x => x.Id).ToList();
            return PartialView("_Users", viewModel);
        }
        [HttpPost]
        public IActionResult CreateUser(string userName, string emailAddress, string password, bool isAdmin)
        {
            var result = _loginLogic.CreateUserByAdmin(userName, emailAddress, password, isAdmin);
            result.Message = _translator.Translate(_configHelper.GetUserConfig(User).UserLanguage, result.Message);
            return Json(result);
        }
        [HttpPost]
        public IActionResult DeleteToken(int tokenId)
        {
            var result = _loginLogic.DeleteUserToken(tokenId);
            return Json(result);
        }
        [HttpPost]
        public IActionResult DeleteUser(int userId)
        {
            var result =_userLogic.DeleteAllAccessToUser(userId) 
                && _configHelper.DeleteUserConfig(userId) 
                && _loginLogic.DeleteUser(userId)
                && _userLogic.DeleteAllHouseholdByChildUserId(userId)
                && _userLogic.DeleteAllHouseholdByParentUserId(userId)
                && _userLogic.DeleteAllAPIKeysByUserId(userId);
            return Json(result);
        }
        [HttpPost]
        public IActionResult UpdateUserAdminStatus(int userId, bool isAdmin)
        {
            var result = _loginLogic.MakeUserAdmin(userId, isAdmin);
            return Json(result);
        }
        [HttpGet]
        public IActionResult GetUserHouseholdModal(int userId)
        {
            var households = _userLogic.GetHouseholdForParentUserId(userId);
            if (households.Any())
            {
                households = households.OrderBy(x => x.UserName).ToList();
            }
            var userCanResetPassword = _loginLogic.GetUserCanResetPassword(userId);
            var viewModel = new UserHouseholdAdminViewModel { Households = households, ParentUserId = userId, UserCanResetPassword = userCanResetPassword };
            return PartialView("_AdminUserHouseholdModal", viewModel);
        }
        [HttpPost]
        public IActionResult RemoveUserFromHousehold(int parentUserId, int childUserId)
        {
            var result = _userLogic.DeleteUserFromHousehold(parentUserId, childUserId);
            return Json(result);
        }
        [HttpPost]
        public IActionResult AddUserToHousehold(int parentUserId, string username)
        {
            var result = _userLogic.AddUserToHousehold(parentUserId, username);
            return Json(result);
        }
        [HttpPost]
        public IActionResult ModifyUserHouseholdPermissions(int parentUserId, int childUserId, List<HouseholdPermission> permissions)
        {
            var result = _userLogic.UpdateUserHousehold(parentUserId, childUserId, permissions);
            return Json(result);
        }
        [HttpPost]
        public IActionResult RevokeUserPassword(int userId)
        {
            var result = _loginLogic.RevokeUserPassword(userId);
            return Json(result);
        }
        [HttpPost]
        public IActionResult ResetUserPassword(int userId)
        {
            var result = _loginLogic.ResetUserPassword(userId);
            return Json(result);
        }
    }
}
