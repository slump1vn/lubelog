function performLogin() {
    let userName = $("#inputUserName").val();
    let userPassword = $("#inputUserPassword").val();
    let isPersistent = $("#inputPersistent").is(":checked");
    $.post('/Login/Login', {userName: userName, password: userPassword, isPersistent: isPersistent}, function (data) {
        if (data.success) {
            //set remember me setting in localStorage
            localStorage.setItem('persistLogin', JSON.stringify(isPersistent));
            //check for redirectURL
            var redirectURL = getRedirectURL().url;
            if (redirectURL.trim() != "") {
                window.location.href = redirectURL;
            } else {
                window.location.href = '/Home';
            }
        } else {
            errorToast(data.message);
        }
    })
}
function checkPersistLoginState() {
    let storedPersistLogin = localStorage.getItem('persistLogin');
    if (storedPersistLogin) {
        $('#inputPersistent').prop('checked', JSON.parse(storedPersistLogin));
    }
}
function requestPasswordReset() {
    let userName = $("#inputUserName").val();
    $.post('/Login/RequestResetPassword', { userName: userName }, function (data) {
        if (data.success) {
            successToast(data.message);
            setTimeout(function () { window.location.href = '/Login/Index' }, 500);
        } else {
            errorToast(data.message);
        }
    })
}
function performPasswordReset() {
    let token = $("#inputToken").val();
    let userPassword = $("#inputUserPassword").val();
    let userEmail = $("#inputEmail").val();
    $.post('/Login/PerformPasswordReset', { password: userPassword, token: token, emailAddress: userEmail }, function (data) {
        if (data.success) {
            successToast(data.message);
            setTimeout(function () { window.location.href = '/Login/Index' }, 500);
        } else {
            errorToast(data.message);
        }
    });
}

function remoteLogin() {
    let currentParams = new URLSearchParams(window.location.search);
    let redirectUrl = currentParams.get('redirectURLBase64');
    if (redirectUrl == null) {
        redirectUrl = '';
    }
    $.get(`/Login/GetRemoteLoginLink?redirectURLBase64=${redirectUrl}`, { redirectURLBase64: redirectUrl }, function (data) {
        if (data) {
            window.location.href = data;
        }
    })
}
