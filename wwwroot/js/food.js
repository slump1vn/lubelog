// Food label check: photos are shrunk in the browser, read by the home AI, checked by the user,
// then analysed on the server (FoodLabelRules). Photos are never stored.
var foodPhotos = [];
var foodMaxPhotos = 3;
var foodNumberFields = ['ServingSize', 'NetQuantity', 'EnergyKcal', 'Fat', 'SaturatedFat', 'TransFat', 'Carbohydrate', 'Sugars', 'Fibre', 'Protein', 'Salt', 'SodiumMg'];

function loadFood() {
    $.get('/Food/Index', function (data) {
        if (data) {
            $("#food-tab-pane").html(data);
            foodPhotos = [];
            showBudgetTimes("#foodHistory", { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
        }
    });
}
function reloadFoodHistory() {
    $.get('/Food/History', function (data) {
        $("#foodHistory").html(data);
        showBudgetTimes("#foodHistory", { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
    });
}
// Phone photos are often 5-12 MB; the AI reads text fine at 1600 px, so send a JPEG of that size.
function shrinkFoodPhoto(file) {
    return new Promise(function (resolve, reject) {
        var url = URL.createObjectURL(file);
        var image = new Image();
        image.onload = function () {
            var scale = Math.min(1, 1600 / Math.max(image.width, image.height));
            var canvas = document.createElement('canvas');
            canvas.width = Math.round(image.width * scale);
            canvas.height = Math.round(image.height * scale);
            canvas.getContext('2d').drawImage(image, 0, 0, canvas.width, canvas.height);
            URL.revokeObjectURL(url);
            canvas.toBlob(function (blob) { blob ? resolve(blob) : reject(); }, 'image/jpeg', 0.88);
        };
        image.onerror = function () {
            URL.revokeObjectURL(url);
            reject();
        };
        image.src = url;
    });
}
function addFoodPhotos(input) {
    var files = Array.from(input.files || []);
    input.value = '';
    files.slice(0, foodMaxPhotos - foodPhotos.length).forEach(function (file) {
        shrinkFoodPhoto(file).then(function (blob) {
            if (foodPhotos.length >= foodMaxPhotos) {
                return;
            }
            foodPhotos.push(blob);
            renderFoodThumbs();
        }).catch(function () {
            errorToast(genericErrorMessage());
        });
    });
}
function renderFoodThumbs() {
    var thumbs = $("#foodThumbs").empty();
    foodPhotos.forEach(function (blob, index) {
        var url = URL.createObjectURL(blob);
        var item = $('<div class="position-relative">');
        item.append($('<img class="rounded border" style="height:96px;width:96px;object-fit:cover" alt="">').attr('src', url));
        item.append($('<button type="button" class="btn btn-sm btn-danger position-absolute top-0 end-0 py-0 px-1" aria-label="x">&times;</button>').on('click', function () {
            foodPhotos.splice(index, 1);
            renderFoodThumbs();
        }));
        thumbs.append(item);
    });
    $("#foodReadButton").toggleClass('d-none', foodPhotos.length == 0);
}
function readFoodLabel() {
    if (!foodPhotos.length) {
        return;
    }
    var form = new FormData();
    foodPhotos.forEach(function (blob, index) {
        form.append('files', blob, `label-${index + 1}.jpg`);
    });
    $("#foodReadButton").prop('disabled', true);
    $("#foodReading").removeClass('d-none').find('span.small').text($("#foodPage").data('reading-label'));
    $.ajax({
        url: '/Food/Read',
        type: 'POST',
        data: form,
        processData: false,
        contentType: false,
        headers: { 'X-Requested-With': 'XMLHttpRequest' },
        success: function (result) {
            $("#foodReadButton").prop('disabled', false);
            $("#foodReading").addClass('d-none');
            if (result.success) {
                showFoodCheck(result.reading);
            } else {
                errorToast(result.message);
            }
        },
        error: function () {
            $("#foodReadButton").prop('disabled', false);
            $("#foodReading").addClass('d-none');
            errorToast(genericErrorMessage());
        }
    });
}
function camelCaseField(field) {
    return field.charAt(0).toLowerCase() + field.slice(1);
}
function showFoodCheck(reading) {
    var form = $("#foodCheckForm");
    form.find('[data-field]').each(function () {
        var field = $(this).data('field');
        var value = reading ? reading[camelCaseField(field)] : null;
        if (this.tagName == 'SELECT') {
            $(this).val(value || $(this).find('option').first().val());
        } else {
            $(this).val(value === null || value === undefined ? '' : value);
        }
        var unsure = reading && (reading.uncertain || []).indexOf(field) >= 0;
        $(this).toggleClass('bg-warning-subtle border-warning', !!unsure);
    });
    form.removeClass('d-none');
    $("#foodResult").empty();
    form[0].scrollIntoView({ behavior: 'smooth', block: 'start' });
}
function collectFoodReading() {
    var reading = { uncertain: [] };
    $("#foodCheckForm [data-field]").each(function () {
        var field = $(this).data('field');
        var value = $(this).val();
        if (field == 'Kind' || field == 'Basis') {
            reading[camelCaseField(field)] = value;
        } else if (foodNumberFields.indexOf(field) >= 0) {
            var number = parseFloat(String(value).replace(',', '.'));
            reading[camelCaseField(field)] = isNaN(number) ? null : number;
        } else {
            reading[camelCaseField(field)] = value || '';
        }
    });
    return reading;
}
function analyzeFoodLabel() {
    var save = $("#foodSave").is(':checked');
    $.ajax({
        url: '/Food/Analyze',
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify({ reading: collectFoodReading(), save: save }),
        success: function (data) {
            showFoodResult(data);
            if (save) {
                reloadFoodHistory();
            }
        },
        error: function () {
            errorToast(genericErrorMessage());
        }
    });
}
function showFoodResult(html) {
    $("#foodResult").html(html);
    showBudgetTimes("#foodResult", { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
    var scanId = $("#foodResultCard").data('scan-id');
    if (scanId && scanId > 0) {
        $.get('/Food/Transactions', function (items) {
            var select = $("#foodLinkSelect");
            (items || []).forEach(function (item) {
                select.append($('<option>').val(item.id).text(item.text));
            });
        });
    }
    $("#foodResult")[0].scrollIntoView({ behavior: 'smooth', block: 'start' });
}
function showFoodScan(id) {
    $.get(`/Food/Scan?id=${encodeURIComponent(id)}`, function (data) {
        $("#foodCheckForm").addClass('d-none');
        showFoodResult(data);
    }).fail(function () {
        errorToast(genericErrorMessage());
    });
}
function resetFoodPage() {
    foodPhotos = [];
    renderFoodThumbs();
    $("#foodCheckForm").addClass('d-none')[0].reset();
    $("#foodResult").empty();
}
function postFoodJson(url, body, done) {
    $.ajax({
        url: url,
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(body),
        success: function (result) {
            if (result.success) {
                successToast(result.message);
                if (done) { done(); }
            } else {
                errorToast(result.message || genericErrorMessage());
            }
        },
        error: function () {
            errorToast(genericErrorMessage());
        }
    });
}
function linkFoodScan(scanId) {
    var transactionId = parseInt($("#foodLinkSelect").val(), 10);
    if (!transactionId) {
        $("#foodLinkSelect").addClass('is-invalid');
        return;
    }
    postFoodJson('/Food/Link', { scanId: scanId, transactionId: transactionId }, function () {
        showFoodScan(scanId);
        reloadFoodHistory();
    });
}
function unlinkFoodScan(scanId) {
    postFoodJson('/Food/Link', { scanId: scanId, transactionId: 0 }, function () {
        showFoodScan(scanId);
        reloadFoodHistory();
    });
}
function deleteFoodScan(scanId) {
    postFoodJson('/Food/DeleteScan', { id: scanId }, function () {
        $("#foodResult").empty();
        reloadFoodHistory();
    });
}
function saveFoodProfile() {
    var allergens = $("#foodProfile input:checked").map(function () { return $(this).val(); }).get();
    postFoodJson('/Food/SaveProfile', { allergens: allergens }, loadFood);
}
function deleteFoodProfile() {
    postFoodJson('/Food/DeleteProfile', {}, loadFood);
}
