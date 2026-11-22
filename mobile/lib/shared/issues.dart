import 'package:dio/dio.dart';

import 'money.dart';

/// Vietnamese for the stable codes the server puts in a problem's "errors"
/// (and for the English messages of the food label API). {0}, {1}... are
/// filled from the issue's args.
const _byCode = <String, String>{
  'gap_not_found': 'Khoản chênh lệch này đã được giải thích hoặc không còn nữa. Tải lại để xem số mới.',
  'amount_exceeds_gap': 'Số tiền lớn hơn phần chênh lệch chưa giải thích.',
  'not_manual': 'Chỉ xóa được các khoản ghi tay.',
  'not_configured': 'Máy chủ chưa cài khóa SePay, nên chưa đồng bộ được.',
  'no_account': 'Chưa lưu số tài khoản chung trên trang web.',
  'account_not_in_sepay': 'SePay không có số tài khoản chung này.',
  'pick_a_date': 'Chọn ngày.',
  'the_amount_must_be_greater_than_0': 'Số tiền phải lớn hơn 0.',
  'the_date_must_be_within_the_last_five_years':
      'Ngày phải nằm trong 5 năm gần đây.',
  'the_category_is_too_long': 'Tên hạng mục quá dài.',
  'the_note_is_too_long': 'Ghi chú quá dài (tối đa 300 ký tự).',
  'the_entry_could_not_be_saved': 'Chưa lưu được. Thử lại sau ít phút.',
  'take_a_photo_first': 'Chụp hoặc chọn một ảnh trước.',
  'each_photo_must_be_smaller_than_8_mb': 'Mỗi ảnh phải nhỏ hơn 8 MB.',
  'the_image_could_not_be_read_please_use_a_png_or_jpg_file':
      'Không đọc được ảnh. Dùng ảnh JPG hoặc PNG.',
  'reading_images_needs_the_home_ai_which_is_not_turned_on':
      'Đọc ảnh cần AI ở nhà, hiện AI chưa bật.',
  'the_home_ai_did_not_answer_please_try_again_later':
      'AI ở nhà chưa trả lời. Thử lại sau ít phút.',
  'this_does_not_look_like_a_receipt_or_a_bank_transfer':
      'Ảnh này không giống hóa đơn hay ảnh chuyển khoản.',
  'the_budget_could_not_be_saved': 'Chưa lưu được ngân sách. Thử lại sau.',
  // Budget rules (BudgetRules.Validate on the server).
  'a_person_can_only_own_one_personal_fund':
      'Mỗi người chỉ có một quỹ cá nhân.',
  'a_personal_fund_must_belong_to_a_member_of_the_household':
      'Quỹ cá nhân phải thuộc một thành viên trong nhà.',
  'approval_threshold_must_be_greater_than_0': 'Mức cần đồng ý phải lớn hơn 0.',
  'budget_cycle_start_day_must_be_between_1_and_31':
      'Ngày bắt đầu kỳ phải từ 1 đến 31.',
  'categories_of_fund_add_up_to_instead_of':
      'Các hạng mục của quỹ {0} cộng lại {1}, chưa bằng {2}.',
  'currency_must_be_a_3_letter_iso_4217_code':
      'Mã tiền tệ phải gồm 3 chữ (ISO 4217).',
  'debt_payments_of_exceed_what_fund_receives':
      'Tiền trả nợ {0} nhiều hơn số quỹ {1} nhận ({2}).',
  'debt_cannot_have_a_negative_balance': 'Khoản nợ {0} không thể có dư nợ âm.',
  'debt_must_be_paid_from_a_debt_fund':
      'Khoản nợ {0} phải trả từ một quỹ trả nợ.',
  'debt_must_have_a_due_day_between_1_and_31':
      'Ngày trả của khoản nợ {0} phải từ 1 đến 31.',
  'debt_must_have_a_monthly_payment_greater_than_0':
      'Khoản nợ {0} cần số trả hằng tháng lớn hơn 0.',
  'debt_must_have_an_interest_rate_between_0_and_100_percent':
      'Lãi suất của khoản nợ {0} phải từ 0 đến 100%.',
  'fund_cannot_have_negative_amounts': 'Quỹ {0} không thể có số âm.',
  'fund_has_a_category_with_a_negative_limit':
      'Quỹ {0} có hạng mục với hạn mức âm.',
  'fund_needs_per_month_but_receives':
      'Quỹ {0} cần {1} mỗi tháng nhưng chỉ nhận {2}.',
  'income_allocates_to_a_fund_that_does_not_exist':
      'Khoản thu {0} chia vào một quỹ không còn tồn tại.',
  'income_has_an_allocation_that_is_not_greater_than_0':
      'Khoản thu {0} có phần chia không lớn hơn 0.',
  'income_must_have_a_payday_between_1_and_31':
      'Ngày nhận của khoản thu {0} phải từ 1 đến 31.',
  'income_must_have_an_amount_greater_than_0': 'Khoản thu {0} phải lớn hơn 0.',
  'income_receives_but_allocates': 'Khoản thu {0} nhận {1} nhưng mới chia {2}.',
  'the_shared_account_number_may_contain_only_letters_and_digit':
      'Số tài khoản chung chỉ gồm chữ và số (4 đến 24 ký tự).',
};

/// English messages of the food label API, which has no codes.
const _byMessage = <String, String>{
  'Reading images needs the home AI, which is not turned on':
      'Đọc ảnh cần AI ở nhà, hiện AI chưa bật.',
  'Take a photo of the label first': 'Chụp ảnh nhãn trước.',
  'Use at most 3 photos': 'Dùng tối đa 3 ảnh.',
  'Each photo must be smaller than 8 MB': 'Mỗi ảnh phải nhỏ hơn 8 MB.',
  'The image could not be read. Please use a PNG or JPG file':
      'Không đọc được ảnh. Dùng ảnh JPG hoặc PNG.',
  'The home AI did not answer. Please try again later':
      'AI ở nhà chưa trả lời. Thử lại sau ít phút.',
  'This does not look like a food label. Try a closer, sharper photo of the nutrition table or the ingredient list': 'Ảnh này không giống nhãn thực phẩm. Chụp gần và rõ hơn bảng dinh dưỡng hoặc phần thành phần.',
  'The scan could not be saved': 'Chưa lưu được kết quả.',
  'Nothing to analyse: no nutrition table and no ingredient list':
      'Không có gì để đánh giá: thiếu cả bảng dinh dưỡng lẫn thành phần.',
  'The table is per serving but the serving size is missing':
      'Bảng tính theo khẩu phần nhưng thiếu khối lượng một khẩu phần.',
  'Saturated fat is higher than total fat':
      'Chất béo bão hòa lớn hơn tổng chất béo.',
  'Trans fat is higher than total fat': 'Chất béo trans lớn hơn tổng chất béo.',
  'Sugars are higher than total carbohydrate':
      'Đường lớn hơn tổng carbohydrate.',
  'The nutrients add up to more than 100 g per 100 g':
      'Các chất cộng lại hơn 100 g trong 100 g.',
  'Energy does not match fat, carbohydrate and protein':
      'Năng lượng không khớp với chất béo, carbohydrate và đạm.',
  'Salt and sodium do not match (salt = sodium × 2.5)':
      'Muối và natri không khớp (muối = natri × 2,5).',
  'The serving is larger than the pack': 'Khẩu phần lớn hơn cả gói.',
};

final _digits = RegExp(r'^-?\d{4,}$');

String _fill(String text, List<String> args) {
  var result = text;
  for (var i = 0; i < args.length; i++) {
    // Amounts come as plain digits; names stay as they are.
    final arg = _digits.hasMatch(args[i])
        ? formatVnd(int.parse(args[i]))
        : args[i];
    result = result.replaceAll('{$i}', arg);
  }
  return result;
}

/// One issue in Vietnamese: the known code, else the known English message,
/// else the English message itself.
String translateIssue(
  String code,
  String message, [
  List<String> args = const [],
]) {
  final known = _byCode[code] ?? _byMessage[message];
  return known == null ? message : _fill(known, args);
}

/// An English message from an API without codes, in Vietnamese when known.
String translateMessage(String message) => _byMessage[message] ?? message;

/// The rule violations of a problem response ("errors"), in Vietnamese.
/// Empty when the response has none.
List<String> problemIssues(DioException error) {
  final data = error.response?.data;
  if (data is! Map) return const [];
  final errors = data['errors'];
  if (errors is! List) return const [];
  return [
    for (final e in errors)
      if (e is Map)
        translateIssue('${e['code'] ?? ''}', '${e['message'] ?? ''}', [
          for (final a in (e['args'] as List?) ?? const []) '$a',
        ]),
  ];
}
